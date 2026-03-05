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
        // ✅ AJOUT: Ligne (Ctrl+Click) + IN/OUT
        // ============================================================
        private readonly ClickLineDetector _lineDetector = new ClickLineDetector
        {
            MinPointsToFit = 6,
            RansacIterations = 250,
            InlierThresholdPx = 6f,
            MinInliers = 6
        };
        private readonly object _lineLock = new();

        public CameraView()
        {
            InitializeComponent();

            cameraPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            cameraPictureBox.BackColor = Color.Black;
            cameraPictureBox.BringToFront();
            distanceLabel.BringToFront();

            // ✅ garde le tracking sur MouseClick
            cameraPictureBox.MouseClick += CameraPictureBox_MouseClick;

            // Dossier photo
            Directory.CreateDirectory(_snapDir);

            // Bouton photo (inchangé)
            button1.Text = "Prendre photo";
            button1.Click += button1_Click;

            // reset ligne optionnel
            KeyPreview = true;
            KeyDown += CameraView_KeyDown;

            // ✅ texte par défaut en NOIR (comme demandé)
            distanceLabel.ForeColor = Color.Black;
            distanceLabel.Text = "Caméra prête. Clique sur un objet pour le suivre. (Ctrl+Click = ligne)";
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

                    distanceLabel.ForeColor = Color.Black;
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

                            UpdateDistanceAndInOut(raw);
                        }
                        else
                        {
                            ThrottledLabel("Objet perdu (reclique).", Color.OrangeRed);
                        }
                    }

                    // bitmap + overlay
                    using var bmp = FrameBitmapConverter.RgbToBitmap24bpp(rgb, w, h);

                    // overlay tracking (comme avant)
                    if (_tracker.IsTracking && _tracker.X >= 0 && _tracker.Y >= 0)
                        FrameBitmapConverter.DrawGreenBox(bmp, _tracker.X, _tracker.Y, BOX_HALF);

                    // overlay ligne ultra léger
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

        // MouseClick: 2 modes sans casser le tracking
        private void CameraPictureBox_MouseClick(object? sender, MouseEventArgs e)
        {
            // MODE LIGNE : Ctrl + Click
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                AddLinePointFromClick(e.Location);
                return;
            }

            // TRACKING (ton code inchangé)
            Bitmap? img;
            lock (_snapshotLock)
            {
                img = _lastBitmapShown == null ? null : (Bitmap)_lastBitmapShown.Clone();
            }

            if (img == null)
            {
                ThrottledLabel("Image pas prête (attends 1-2 secondes).", Color.Black);
                return;
            }

            var (x, y) = TranslateZoomMousePositionToImagePixel(cameraPictureBox, img, e.Location);
            img.Dispose();

            if (x < 0 || y < 0)
            {
                ThrottledLabel("Clique dans l'image (pas dans les bandes).", Color.Black);
                return;
            }

            if (!_camera.TryGetAlignedFrames(500, out var rgb, out var depthU16))
            {
                ThrottledLabel("Frame non dispo (reclique).", Color.Black);
                return;
            }

            bool started = _tracker.TryStart(rgb, _camera.ColorW, _camera.ColorH, x, y);
            if (!started)
            {
                ThrottledLabel("Impossible de créer template (bord image).", Color.Black);
                return;
            }

            ushort raw = DistanceCalculator.MedianDepthRaw(
                depthU16, _camera.DepthW, _camera.DepthH,
                _tracker.X, _tracker.Y, DIST_RADIUS);

            UpdateDistanceAndInOut(raw);

            Log($"Tracking ON at ({_tracker.X},{_tracker.Y}), tpl={_tracker.TemplateSize} search={_tracker.SearchRadius}");
        }

        // Ajout point ligne depuis clic (Zoom OK)
        private void AddLinePointFromClick(Point clickLocation)
        {
            Bitmap? img;
            lock (_snapshotLock)
            {
                img = _lastBitmapShown == null ? null : (Bitmap)_lastBitmapShown.Clone();
            }

            if (img == null)
            {
                ThrottledLabel("Image pas prête (attends 1-2 secondes).", Color.Black);
                return;
            }

            var (x, y) = TranslateZoomMousePositionToImagePixel(cameraPictureBox, img, clickLocation);
            img.Dispose();

            if (x < 0 || y < 0)
            {
                ThrottledLabel("Ctrl+Clique dans l'image (pas dans les bandes).", Color.Black);
                return;
            }

            bool hasLineNow;
            int count;
            lock (_lineLock)
            {
                _lineDetector.AddClick(new PointF(x, y));
                hasLineNow = _lineDetector.HasLine;
                count = _lineDetector.Samples.Count;
            }

            if (hasLineNow)
                ThrottledLabel("✅ Ligne détectée (Ctrl+Click pour ajouter / R reset)", Color.Black);
            else
                ThrottledLabel($"Mode ligne: {count}/{_lineDetector.MinPointsToFit} points", Color.Black);
        }

        // Overlay ligne + points (optimisé)
        private void DrawLineOverlayOnBitmap(Bitmap bmp)
        {
            bool hasLine;

            lock (_lineLock)
            {
                hasLine = _lineDetector.HasLine;
                if (!hasLine && _lineDetector.Samples.Count == 0)
                    return;
            }

            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

            // Points
            lock (_lineLock)
            {
                for (int i = 0; i < _lineDetector.Samples.Count; i++)
                {
                    var p = _lineDetector.Samples[i];
                    g.FillEllipse(Brushes.Lime, p.X - 2, p.Y - 2, 4, 4);
                }
            }

            if (!hasLine) return;

            var bounds = new RectangleF(0, 0, bmp.Width - 1, bmp.Height - 1);
            if (_lineDetector.TryGetSegmentWithin(bounds, out var a, out var b))
            {
                using var pen = new Pen(Color.Red, 2f);
                g.DrawLine(pen, a, b);
            }
        }

        // Distance + IN/OUT (seule partie en couleur)
        private void UpdateDistanceAndInOut(ushort raw)
        {
            if (raw == 0)
            {
                ThrottledLabel("Objet suivi, mais profondeur invalide.", Color.OrangeRed);
                return;
            }

            var (m, cm) = DistanceCalculator.RawToMetersCm(raw, _camera.DepthUnits);

            // throttle UI
            long now = DateTime.UtcNow.Ticks;
            if (now - _lastUiTicks < UiMinTicks) return;
            _lastUiTicks = now;

            bool hasInOut = TryComputeInOut_LeftIsIn(_tracker.X, _tracker.Y, out bool isIn);

            SafeUI(() =>
            {
                if (!hasInOut)
                {
                    distanceLabel.ForeColor = Color.Black;
                    distanceLabel.Text = $"{m:0.000} m  ({cm:0.0} cm)  |  Ligne: non définie (Ctrl+Click)";
                    return;
                }

                // ✅ seulement IN/OUT en couleur
                distanceLabel.ForeColor = isIn ? Color.LimeGreen : Color.Red;
                distanceLabel.Text = $"{m:0.000} m  ({cm:0.0} cm)  |  " + (isIn ? "IN ✅" : "OUT ❌");
            });
        }

        // Test gauche/droite stable : direction forcée vers le haut + cross
        private bool TryComputeInOut_LeftIsIn(int ballX, int ballY, out bool isIn)
        {
            isIn = false;

            ClickLineDetector.LineModel line;
            lock (_lineLock)
            {
                if (!_lineDetector.HasLine) return false;
                line = _lineDetector.Line;
            }

            float x0 = line.Point.X;
            float y0 = line.Point.Y;
            float dx = line.Direction.X;
            float dy = line.Direction.Y;

            // Force direction "vers le haut" (dy < 0)
            if (dy > 0f)
            {
                dx = -dx;
                dy = -dy;
            }

            float vx = ballX - x0;
            float vy = ballY - y0;

            // cross = v x dir = vx*dy - vy*dx
            float cross = vx * dy - vy * dx;

            // gauche => cross > 0
            isIn = cross > 0f;
            return true;
        }

        // Throttled label qui garde la couleur demandée (par défaut noir)
        private void ThrottledLabel(string text, Color color)
        {
            long now = DateTime.UtcNow.Ticks;
            if (now - _lastUiTicks >= UiMinTicks)
            {
                _lastUiTicks = now;
                SafeUI(() =>
                {
                    distanceLabel.ForeColor = color;
                    distanceLabel.Text = text;
                });
            }
        }

        // Mapping clic Zoom -> pixel image
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
                SafeUI(() =>
                {
                    distanceLabel.ForeColor = Color.Black;
                    distanceLabel.Text = "Ligne reset ✅ (Ctrl+Click pour redéfinir)";
                });
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
            SafeUI(() =>
            {
                distanceLabel.ForeColor = Color.Black;
                distanceLabel.Text = s;
            });
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

                lock (_snapshotLock)
                {
                    if (_lastBitmapShown != null)
                        snap = (Bitmap)_lastBitmapShown.Clone();
                }

                if (snap == null)
                {
                    SafeUI(() =>
                    {
                        distanceLabel.ForeColor = Color.Black;
                        distanceLabel.Text = "Pas d'image à enregistrer (attends la caméra).";
                    });
                    return;
                }

                string fileName = $"rs_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                string fullPath = Path.Combine(_snapDir, fileName);

                snap.Save(fullPath, ImageFormat.Png);
                snap.Dispose();

                SafeUI(() =>
                {
                    distanceLabel.ForeColor = Color.Black;
                    distanceLabel.Text = $"Photo enregistrée: {fileName}";
                });
                Debug.WriteLine("Saved snapshot: " + fullPath);
            }
            catch (Exception ex)
            {
                SafeUI(() =>
                {
                    distanceLabel.ForeColor = Color.OrangeRed;
                    distanceLabel.Text = "Erreur photo: " + ex.Message;
                });
                Debug.WriteLine("Snapshot error: " + ex);
            }
        }
    }
}