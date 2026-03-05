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

        // Tracker manuel (click sur un objet) -> NE CHANGE PAS
        private readonly TemplateTracker _tracker = new TemplateTracker();

        private CancellationTokenSource? _cts;
        private Task? _task;

        // overlay / distance
        private const int BOX_HALF = 12;
        private const int DIST_RADIUS = 2;

        // UI throttle (10 Hz)
        private long _lastUiTicks = 0;
        private static readonly long UiMinTicks = TimeSpan.TicksPerSecond / 10;

        // Dernière image affichée (pour mapping clic -> pixel)
        private Bitmap? _lastBitmapShown;

        // ====== PHOTO / SNAPSHOT ======
        private readonly string _snapDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RealSense_Captures");
        private readonly object _snapshotLock = new();

        // ===== Ligne (Ctrl+Click) =====
        private readonly ClickLineDetector _lineDetector = new ClickLineDetector
        {
            MinPointsToFit = 6,
            RansacIterations = 250,
            InlierThresholdPx = 6f,
            MinInliers = 6
        };
        private readonly object _lineLock = new();

        // ===== AUTO BALL : detect -> template tracking auto =====
        private readonly BallDetector _ballDetector = new BallDetector();
        private readonly TemplateTracker _autoTracker = new TemplateTracker(); // séparé du tracker click
        private readonly AutoTemplateFollower _autoFollower;
        private readonly TrajectoryTracker _traj = new TrajectoryTracker();
        private readonly ImpactDetector _impact = new ImpactDetector();

        private bool _autoEnabled = true;

        // impact affichage
        private long _lastImpactTicks = 0;
        private const int ImpactCooldownMs = 800;

        private PointF? _impactPoint = null;
        private long _impactPointTicks = 0;
        private const int ImpactMarkerMs = 1500;

        public CameraView()
        {
            InitializeComponent();

            _autoFollower = new AutoTemplateFollower(_ballDetector, _autoTracker)
            {
                RoiHalfSize = 220,
                ReacquireEveryNFrames = 2,
                MinConfirmFrames = 2
            };

            cameraPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            cameraPictureBox.BackColor = Color.Black;
            cameraPictureBox.BringToFront();
            distanceLabel.BringToFront();

            // ✅ Click normal = tracking manuel (comme avant)
            cameraPictureBox.MouseClick += CameraPictureBox_MouseClick;

            Directory.CreateDirectory(_snapDir);

            // ✅ button1 = photo (inchangé)
            button1.Text = "Prendre photo";
            button1.Click += button1_Click;

            KeyPreview = true;
            KeyDown += CameraView_KeyDown;

            distanceLabel.ForeColor = Color.Black;
            distanceLabel.Text = "OK. Click=tracker | Ctrl+Click=ligne | Shift+Click=calibrer balle | A=Auto ON/OFF | R=reset ligne";
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

                SetStatus("OK. Click=tracker | Ctrl+Click=ligne | Shift+Click=calibrer balle | A=Auto ON/OFF");
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
                _autoTracker.Stop();

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

                    // ===== TRACKING MANUEL (comme avant) =====
                    if (_tracker.IsTracking)
                    {
                        bool ok = _tracker.TryUpdate(rgb, w, h);

                        if (ok)
                        {
                            ushort raw = DistanceCalculator.MedianDepthRaw(
                                depthU16, _camera.DepthW, _camera.DepthH,
                                _tracker.X, _tracker.Y, DIST_RADIUS);

                            UpdateDistanceAndInOut(raw, _tracker.X, _tracker.Y, "TRACK");
                        }
                        else
                        {
                            ThrottledLabel("Objet perdu (reclique).", Color.OrangeRed);
                        }
                    }

                    // ===== IMAGE + OVERLAYS =====
                    using var bmp = FrameBitmapConverter.RgbToBitmap24bpp(rgb, w, h);

                    // overlay tracking manuel (vert)
                    if (_tracker.IsTracking && _tracker.X >= 0 && _tracker.Y >= 0)
                        FrameBitmapConverter.DrawGreenBox(bmp, _tracker.X, _tracker.Y, BOX_HALF);

                    // ===== AUTO BALL (detect -> template auto) =====
                    if (_autoEnabled)
                    {
                        bool okAuto = _autoFollower.TryUpdate(rgb, w, h, bmp, out int bx, out int by);

                        if (okAuto && bx >= 0 && by >= 0)
                        {
                            // Dessin cercle auto (bleu)
                            using (var g = Graphics.FromImage(bmp))
                            using (var pen = new Pen(Color.DeepSkyBlue, 2f))
                            {
                                g.DrawEllipse(pen, bx - 12, by - 12, 24, 24);
                            }

                            // Trajectoire + distance + impact
                            long t = DateTime.UtcNow.Ticks;
                            _traj.Add(new PointF(bx, by), t);

                            ushort rawBall = DistanceCalculator.MedianDepthRaw(
                                depthU16, _camera.DepthW, _camera.DepthH,
                                bx, by, DIST_RADIUS);

                            UpdateDistanceAndInOut(rawBall, bx, by, "AUTO");

                            if (_traj.TryGetPreviousVelocity(out var vPrev) &&
                                _traj.TryGetVelocity(out var vNow) &&
                                _traj.TryGetLastTwoPositions(out var pPrev, out var pNow))
                            {
                                float movePx = Distance(pPrev, pNow);

                                if (_impact.TryDetectFirstImpact(vPrev, vNow, movePx))
                                {
                                    if (DateTime.UtcNow.Ticks - _lastImpactTicks >
                                        TimeSpan.FromMilliseconds(ImpactCooldownMs).Ticks)
                                    {
                                        _lastImpactTicks = DateTime.UtcNow.Ticks;

                                        if (InOutJudge.TryIsIn(_lineDetector, _lineLock, new PointF(bx, by), out bool isIn))
                                        {
                                            _impactPoint = new PointF(bx, by);
                                            _impactPointTicks = DateTime.UtcNow.Ticks;

                                            SafeUI(() =>
                                            {
                                                distanceLabel.ForeColor = isIn ? Color.LimeGreen : Color.Red;
                                                distanceLabel.Text = "IMPACT: " + (isIn ? "IN ✅" : "OUT ❌");
                                            });
                                        }
                                        else
                                        {
                                            SafeUI(() =>
                                            {
                                                distanceLabel.ForeColor = Color.OrangeRed;
                                                distanceLabel.Text = "IMPACT détecté mais ligne absente (Ctrl+Click)";
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // overlay ligne
                    DrawLineOverlayOnBitmap(bmp);

                    // croix jaune impact
                    DrawImpactMarker(bmp);

                    // clone safe pour UI
                    var toShow = (Bitmap)bmp.Clone();

                    SafeUI(() =>
                    {
                        var old = cameraPictureBox.Image;
                        cameraPictureBox.Image = toShow;
                        old?.Dispose();

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

        private void DrawImpactMarker(Bitmap bmp)
        {
            if (!_impactPoint.HasValue) return;

            long dt = DateTime.UtcNow.Ticks - _impactPointTicks;
            if (dt > TimeSpan.FromMilliseconds(ImpactMarkerMs).Ticks)
            {
                _impactPoint = null;
                return;
            }

            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.Yellow, 3f);

            var p = _impactPoint.Value;
            g.DrawLine(pen, p.X - 10, p.Y - 10, p.X + 10, p.Y + 10);
            g.DrawLine(pen, p.X - 10, p.Y + 10, p.X + 10, p.Y - 10);
        }

        private void CameraPictureBox_MouseClick(object? sender, MouseEventArgs e)
        {
            // Shift+Click = calibrer couleur balle
            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                CalibrateBallColorFromClick(e.Location);
                return;
            }

            // Ctrl+Click = ajouter point ligne
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                AddLinePointFromClick(e.Location);
                return;
            }

            // sinon : tracking manuel (inchangé)
            StartTrackingFromClick(e.Location);
        }

        private void StartTrackingFromClick(Point clickLocation)
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

            UpdateDistanceAndInOut(raw, _tracker.X, _tracker.Y, "TRACK");
        }

        private void CalibrateBallColorFromClick(Point clickLocation)
        {
            Bitmap? img;
            lock (_snapshotLock)
            {
                img = _lastBitmapShown == null ? null : (Bitmap)_lastBitmapShown.Clone();
            }

            if (img == null)
            {
                ThrottledLabel("Image pas prête pour calibration.", Color.Black);
                return;
            }

            var (x, y) = TranslateZoomMousePositionToImagePixel(cameraPictureBox, img, clickLocation);
            if (x < 0 || y < 0)
            {
                img.Dispose();
                ThrottledLabel("Shift+Clique dans l'image.", Color.Black);
                return;
            }

            Color c = img.GetPixel(x, y);
            img.Dispose();

            _ballDetector.TargetR = c.R;
            _ballDetector.TargetG = c.G;
            _ballDetector.TargetB = c.B;

            _ballDetector.TolR = 100;
            _ballDetector.TolG = 100;
            _ballDetector.TolB = 100;

            _ballDetector.MinBlobPixels = 60;

            _autoTracker.Stop();
            _autoFollower.Reset();
            _traj.Reset();
            _impact.Reset();

            ThrottledLabel($"Calibration balle: R{c.R} G{c.G} B{c.B}", Color.Black);
        }

        private void AddLinePointFromClick(Point clickLocation)
        {
            Bitmap? img;
            lock (_snapshotLock)
            {
                img = _lastBitmapShown == null ? null : (Bitmap)_lastBitmapShown.Clone();
            }

            if (img == null)
            {
                ThrottledLabel("Image pas prête.", Color.Black);
                return;
            }

            var (x, y) = TranslateZoomMousePositionToImagePixel(cameraPictureBox, img, clickLocation);
            img.Dispose();

            if (x < 0 || y < 0)
            {
                ThrottledLabel("Ctrl+Clique dans l'image.", Color.Black);
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

            ThrottledLabel(hasLineNow
                ? "✅ Ligne détectée (Ctrl+Click pour ajouter / R reset)"
                : $"Mode ligne: {count}/{_lineDetector.MinPointsToFit} points", Color.Black);
        }

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

        private void UpdateDistanceAndInOut(ushort raw, int x, int y, string prefix)
        {
            if (raw == 0) return;

            var (m, cm) = DistanceCalculator.RawToMetersCm(raw, _camera.DepthUnits);

            long now = DateTime.UtcNow.Ticks;
            if (now - _lastUiTicks < UiMinTicks) return;
            _lastUiTicks = now;

            bool hasInOut = TryComputeInOut_LeftIsIn(x, y, out bool isIn);

            SafeUI(() =>
            {
                if (!hasInOut)
                {
                    distanceLabel.ForeColor = Color.Black;
                    distanceLabel.Text = $"{prefix}: {m:0.000} m ({cm:0.0} cm) | Trace ligne (Ctrl+Click)";
                    return;
                }

                distanceLabel.ForeColor = isIn ? Color.LimeGreen : Color.Red;
                distanceLabel.Text = $"{prefix}: {m:0.000} m ({cm:0.0} cm) | " + (isIn ? "IN ✅" : "OUT ❌");
            });
        }

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

            if (dy > 0f) { dx = -dx; dy = -dy; }

            float vx = ballX - x0;
            float vy = ballY - y0;
            float cross = vx * dy - vy * dx;

            isIn = cross > 0f;
            return true;
        }

        private void CameraView_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.R)
            {
                lock (_lineLock) _lineDetector.Clear();
                ThrottledLabel("Ligne reset ✅ (Ctrl+Click)", Color.Black);
            }
            else if (e.KeyCode == Keys.A)
            {
                _autoEnabled = !_autoEnabled;
                _autoTracker.Stop();
                _autoFollower.Reset();
                _traj.Reset();
                _impact.Reset();
                ThrottledLabel("AUTO BALL: " + (_autoEnabled ? "ON" : "OFF"), Color.Black);
            }
        }

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

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
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
                    ThrottledLabel("Pas d'image à enregistrer.", Color.Black);
                    return;
                }

                string fileName = $"rs_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                string fullPath = Path.Combine(_snapDir, fileName);

                snap.Save(fullPath, ImageFormat.Png);
                snap.Dispose();

                ThrottledLabel($"Photo enregistrée: {fileName}", Color.Black);
                Debug.WriteLine("Saved snapshot: " + fullPath);
            }
            catch (Exception ex)
            {
                ThrottledLabel("Erreur photo: " + ex.Message, Color.OrangeRed);
                Debug.WriteLine("Snapshot error: " + ex);
            }
        }
    }
}