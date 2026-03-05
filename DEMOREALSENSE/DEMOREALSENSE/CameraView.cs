using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DEMOREALSENSE
{
    public partial class CameraView : Form
    {
        private readonly RealSenseCameraService _camera = new RealSenseCameraService();
        private readonly TemplateTracker _tracker = new TemplateTracker();

        private CancellationTokenSource? _cts;
        private Task? _task;

        // overlay / distance
        private const int BOX_HALF = 12;
        private const int DIST_RADIUS = 2;

        // UI throttle (10 Hz)
        private long _lastUiTicks = 0;
        private static readonly long UiMinTicks = TimeSpan.TicksPerSecond / 10;

        // Dernière image (pour mapping click -> pixel)
        private Bitmap? _lastBitmapShown;

        // ====== PHOTO / SNAPSHOT ======
        private readonly string _snapDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RealSense_Captures");

        private readonly object _snapshotLock = new();

        // ============================================================
        // ✅ AJOUT: Détection de ligne par clics (Ctrl + Click)
        // ============================================================
        private readonly ClickLineDetector _lineDetector = new ClickLineDetector
        {
            MinPointsToFit = 6,
            RansacIterations = 250,
            InlierThresholdPx = 6f,
            MinInliers = 6
        };

        private readonly object _lineLock = new(); // thread-safe

        public CameraView()
        {
            InitializeComponent();

            cameraPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            cameraPictureBox.BackColor = Color.Black;
            cameraPictureBox.BringToFront();
            distanceLabel.BringToFront();

            // ✅ On garde ton MouseClick (tracking) -> inchangé
            cameraPictureBox.MouseClick += CameraPictureBox_MouseClick;

            // Dossier photo
            Directory.CreateDirectory(_snapDir);

            // Bouton photo (tu l'as déjà posé dans le designer)
            button1.Text = "Prendre photo";
            button1.Click += button1_Click;

            // ✅ (Optionnel) Reset de la ligne avec R, sans casser
            KeyPreview = true;
            KeyDown += CameraView_KeyDown;

            distanceLabel.Text = "Caméra prête. Clique sur un objet pour le suivre. (Ctrl+Click = mode ligne)";
            Log("UI Ready");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Stop();
            base.OnFormClosing(e);
        }

        private void Start()
        {
            try
            {
                Stop();

                SetStatus("Start pipeline...");
                _camera.Start(640, 480, 30);

                _cts = new CancellationTokenSource();
                _task = Task.Run(() => Loop(_cts.Token), _cts.Token);

                SetStatus("OK. Clique sur un objet pour le suivre. (Ctrl+Click = ligne)");
            }
            catch (Exception ex)
            {
                SetStatus("Start error: " + ex.Message);
                Log(ex.ToString());
            }
        }

        private void Stop()
        {
            try
            {
                _cts?.Cancel();
                try { _task?.Wait(500); } catch { }

                _task = null;
                _cts?.Dispose();
                _cts = null;

                _tracker.Stop();
                _camera.Stop();

                SafeUI(() =>
                {
                    var old = cameraPictureBox.Image;
                    cameraPictureBox.Image = null;
                    old?.Dispose();

                    lock (_snapshotLock)
                    {
                        _lastBitmapShown?.Dispose();
                        _lastBitmapShown = null;
                    }

                    distanceLabel.Text = "Arrêté.";
                });
            }
            catch { }
        }

        private void Loop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_camera.TryGetAlignedFrames(2000, out var rgb, out var depthU16))
                        continue;

                    int w = _camera.ColorW;
                    int h = _camera.ColorH;

                    // tracking update
                    if (_tracker.IsTracking)
                    {
                        bool ok = _tracker.TryUpdate(rgb, w, h);

                        if (ok)
                        {
                            ushort raw = DistanceCalculator.MedianDepthRaw(
                                depthU16, _camera.DepthW, _camera.DepthH,
                                _tracker.X, _tracker.Y, DIST_RADIUS);

                            UpdateDistanceLabel(raw);
                        }
                        else
                        {
                            ThrottledLabel("Objet perdu (reclique).");
                        }
                    }

                    // bitmap + overlay
                    using var bmp = FrameBitmapConverter.RgbToBitmap24bpp(rgb, w, h);

                    // ✅ overlay tracking (comme avant)
                    if (_tracker.IsTracking && _tracker.X >= 0 && _tracker.Y >= 0)
                        FrameBitmapConverter.DrawGreenBox(bmp, _tracker.X, _tracker.Y, BOX_HALF);

                    // ✅ AJOUT overlay ligne (dans le bitmap, pas dans Paint)
                    DrawLineOverlayOnBitmap(bmp);

                    // clone safe pour UI
                    var toShow = (Bitmap)bmp.Clone();

                    SafeUI(() =>
                    {
                        var old = cameraPictureBox.Image;
                        cameraPictureBox.Image = toShow;
                        old?.Dispose();

                        // stocker une copie pour le clic + snapshot
                        lock (_snapshotLock)
                        {
                            _lastBitmapShown?.Dispose();
                            _lastBitmapShown = (Bitmap)toShow.Clone();
                        }
                    });
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    Log("Loop error: " + ex);
                    Thread.Sleep(30);
                }
            }
        }

        // ============================================================
        // ✅ MouseClick: 2 modes sans casser le tracking
        // - Click normal => tracking (ton code inchangé)
        // - Ctrl + Click => ajout point de ligne
        // ============================================================
        private void CameraPictureBox_MouseClick(object? sender, MouseEventArgs e)
        {
            // ✅ MODE LIGNE : Ctrl + Click
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                AddLinePointFromClick(e.Location);
                return;
            }

            // ==========================
            // TRACKING (INCHANGÉ)
            // ==========================
            Bitmap? img;
            lock (_snapshotLock)
            {
                img = _lastBitmapShown == null ? null : (Bitmap)_lastBitmapShown.Clone();
            }

            if (img == null)
            {
                distanceLabel.Text = "Image pas prête (attends 1-2 secondes).";
                return;
            }

            var (x, y) = TranslateZoomMousePositionToImagePixel(cameraPictureBox, img, e.Location);
            img.Dispose();

            if (x < 0 || y < 0)
            {
                distanceLabel.Text = "Clique dans l'image (pas dans les bandes).";
                return;
            }

            // On a besoin d'une frame récente pour initialiser tracker :
            if (!_camera.TryGetAlignedFrames(500, out var rgb, out var depthU16))
            {
                distanceLabel.Text = "Frame non dispo (reclique).";
                return;
            }

            bool started = _tracker.TryStart(rgb, _camera.ColorW, _camera.ColorH, x, y);
            if (!started)
            {
                distanceLabel.Text = "Impossible de créer template (bord image).";
                return;
            }

            // Distance immédiate
            ushort raw = DistanceCalculator.MedianDepthRaw(
                depthU16, _camera.DepthW, _camera.DepthH,
                _tracker.X, _tracker.Y, DIST_RADIUS);

            UpdateDistanceLabel(raw);

            Log($"Tracking ON at ({_tracker.X},{_tracker.Y}), tpl={_tracker.TemplateSize} search={_tracker.SearchRadius}");
        }

        // ============================================================
        // ✅ AJOUT: ajoute un point de ligne depuis un clic (Zoom OK)
        // ============================================================
        private void AddLinePointFromClick(Point clickLocation)
        {
            Bitmap? img;
            lock (_snapshotLock)
            {
                img = _lastBitmapShown == null ? null : (Bitmap)_lastBitmapShown.Clone();
            }

            if (img == null)
            {
                distanceLabel.Text = "Image pas prête (attends 1-2 secondes).";
                return;
            }

            var (x, y) = TranslateZoomMousePositionToImagePixel(cameraPictureBox, img, clickLocation);
            img.Dispose();

            if (x < 0 || y < 0)
            {
                distanceLabel.Text = "Ctrl+Clique dans l'image (pas dans les bandes).";
                return;
            }

            lock (_lineLock)
            {
                _lineDetector.AddClick(new PointF(x, y));
            }

            // feedback
            ThrottledLabel(_lineDetector.HasLine
                ? "✅ Ligne détectée (Ctrl+Click pour ajouter / R pour reset)"
                : $"Mode ligne: {_lineDetector.Samples.Count}/{_lineDetector.MinPointsToFit} points");
        }

        // ============================================================
        // ✅ AJOUT: dessine la ligne + points dans le bitmap (safe)
        // ============================================================
        private void DrawLineOverlayOnBitmap(Bitmap bmp)
        {
            ClickLineDetector.LineModel line;
            bool hasLine;
            PointF[] pts;

            lock (_lineLock)
            {
                hasLine = _lineDetector.HasLine;
                line = _lineDetector.Line;
                pts = _lineDetector.Samples.Count == 0 ? Array.Empty<PointF>() : new System.Collections.Generic.List<PointF>(_lineDetector.Samples).ToArray();
            }

            if (!hasLine && pts.Length == 0) return;

            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // points
            for (int i = 0; i < pts.Length; i++)
            {
                var p = pts[i];
                g.FillEllipse(Brushes.Lime, p.X - 3, p.Y - 3, 6, 6);
            }

            if (!hasLine) return;

            var bounds = new RectangleF(0, 0, bmp.Width - 1, bmp.Height - 1);

            // Segment image
            if (_lineDetector.TryGetSegmentWithin(bounds, out var a, out var b))
            {
                using var pen = new Pen(Color.Red, 3f);
                g.DrawLine(pen, a, b);
            }
        }

        private void UpdateDistanceLabel(ushort raw)
        {
            if (raw == 0)
            {
                ThrottledLabel("Objet suivi, mais profondeur invalide.");
                return;
            }

            var (m, cm) = DistanceCalculator.RawToMetersCm(raw, _camera.DepthUnits);

            long now = DateTime.UtcNow.Ticks;
            if (now - _lastUiTicks >= UiMinTicks)
            {
                _lastUiTicks = now;
                SafeUI(() => distanceLabel.Text = $"{m:0.000} m  ({cm:0.0} cm)");
            }
        }

        private void ThrottledLabel(string text)
        {
            long now = DateTime.UtcNow.Ticks;
            if (now - _lastUiTicks >= UiMinTicks)
            {
                _lastUiTicks = now;
                SafeUI(() => distanceLabel.Text = text);
            }
        }

        // Mapping clic Zoom -> pixel image (utilise la vraie taille bitmap)
        private static (int x, int y) TranslateZoomMousePositionToImagePixel(PictureBox pb, Image img, Point mouse)
        {
            float imageAspect = (float)img.Width / img.Height;
            float boxAspect = (float)pb.Width / pb.Height;

            int drawWidth, drawHeight;
            int offsetX = 0, offsetY = 0;

            if (imageAspect > boxAspect)
            {
                drawWidth = pb.Width;
                drawHeight = (int)(pb.Width / imageAspect);
                offsetY = (pb.Height - drawHeight) / 2;
            }
            else
            {
                drawHeight = pb.Height;
                drawWidth = (int)(pb.Height * imageAspect);
                offsetX = (pb.Width - drawWidth) / 2;
            }

            int x = mouse.X - offsetX;
            int y = mouse.Y - offsetY;

            if (x < 0 || y < 0 || x >= drawWidth || y >= drawHeight) return (-1, -1);

            int imgX = (int)(x * (img.Width / (float)drawWidth));
            int imgY = (int)(y * (img.Height / (float)drawHeight));
            return (imgX, imgY);
        }

        private void CameraView_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.R)
            {
                lock (_lineLock) _lineDetector.Clear();
                ThrottledLabel("Ligne reset ✅ (Ctrl+Click pour redéfinir)");
            }
        }

        private void SafeUI(Action a)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(a);
                else a();
            }
            catch { }
        }

        private void SetStatus(string s)
        {
            SafeUI(() => distanceLabel.Text = s);
            Log(s);
        }

        private void Log(string s)
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {s}");
        }

        // ====== BOUTON PHOTO ======
        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                Bitmap? snap = null;

                // On prend la DERNIERE image affichée (avec overlay)
                lock (_snapshotLock)
                {
                    if (_lastBitmapShown != null)
                        snap = (Bitmap)_lastBitmapShown.Clone();
                }

                if (snap == null)
                {
                    distanceLabel.Text = "Pas d'image à enregistrer (attends la caméra).";
                    return;
                }

                string fileName = $"rs_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                string fullPath = Path.Combine(_snapDir, fileName);

                snap.Save(fullPath, ImageFormat.Png);
                snap.Dispose();

                distanceLabel.Text = $"Photo enregistrée: {fileName}";
                Debug.WriteLine("Saved snapshot: " + fullPath);
            }
            catch (Exception ex)
            {
                distanceLabel.Text = "Erreur photo: " + ex.Message;
                Debug.WriteLine("Snapshot error: " + ex);
            }
        }
    }
}