using System;
using System.Diagnostics;
using System.Drawing;
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

        public CameraView()
        {
            InitializeComponent();

            cameraPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            cameraPictureBox.BackColor = Color.Black;
            cameraPictureBox.BringToFront();
            distanceLabel.BringToFront();

            cameraPictureBox.MouseClick += CameraPictureBox_MouseClick;

            distanceLabel.Text = "Caméra prête. Clique sur un objet pour le suivre.";
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

                SetStatus("OK. Clique sur un objet pour le suivre.");
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

                    _lastBitmapShown?.Dispose();
                    _lastBitmapShown = null;

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
                            ushort raw = DistanceCalculator.MedianDepthRaw(depthU16, _camera.DepthW, _camera.DepthH, _tracker.X, _tracker.Y, DIST_RADIUS);
                            UpdateDistanceLabel(raw);
                        }
                        else
                        {
                            ThrottledLabel("Objet perdu (reclique).");
                        }
                    }

                    // bitmap + overlay
                    using var bmp = FrameBitmapConverter.RgbToBitmap24bpp(rgb, w, h);

                    if (_tracker.IsTracking && _tracker.X >= 0 && _tracker.Y >= 0)
                        FrameBitmapConverter.DrawGreenBox(bmp, _tracker.X, _tracker.Y, BOX_HALF);

                    // clone safe pour UI
                    var toShow = (Bitmap)bmp.Clone();

                    SafeUI(() =>
                    {
                        var old = cameraPictureBox.Image;
                        cameraPictureBox.Image = toShow;
                        old?.Dispose();

                        _lastBitmapShown?.Dispose();
                        _lastBitmapShown = (Bitmap)toShow.Clone(); // pour mapping clic fiable
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

        private void CameraPictureBox_MouseClick(object? sender, MouseEventArgs e)
        {
            // On mappe le clic vers pixel image
            if (_lastBitmapShown == null)
            {
                distanceLabel.Text = "Image pas prête (attends 1-2 secondes).";
                return;
            }

            var (x, y) = TranslateZoomMousePositionToImagePixel(cameraPictureBox, _lastBitmapShown, e.Location);
            if (x < 0 || y < 0)
            {
                distanceLabel.Text = "Clique dans l'image (pas dans les bandes).";
                return;
            }

            // On a besoin d'une frame récente pour initialiser tracker :
            // => on relit une frame rapide (timeout court)
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
            ushort raw = DistanceCalculator.MedianDepthRaw(depthU16, _camera.DepthW, _camera.DepthH, _tracker.X, _tracker.Y, DIST_RADIUS);
            UpdateDistanceLabel(raw);

            Log($"Tracking ON at ({_tracker.X},{_tracker.Y}), tpl={_tracker.TemplateSize} search={_tracker.SearchRadius}");
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
    }
}