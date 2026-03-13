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
        private readonly TemplateTracker _autoTracker = new TemplateTracker();

        private readonly ClickLineDetector _lineDetector = new ClickLineDetector
        {
            MinPointsToFit = 6,
            RansacIterations = 250,
            InlierThresholdPx = 6f,
            MinInliers = 6
        };
        private readonly object _lineLock = new();

        private readonly BallDetector _ballDetector = new BallDetector();
        private readonly AutoTemplateFollower _autoFollower;
        private readonly TrajectoryTracker _traj = new TrajectoryTracker();
        private readonly ImpactDetector _impact = new ImpactDetector();
        private readonly GroundEstimator _ground = new GroundEstimator { NearGroundPx = 35f, AboveGroundPx = 80f };

        // OUT = 5s (si tu utilises InOutLatch comme tu l'as collé)
        private readonly InOutLatch _inOutLatch = new InOutLatch { OutHoldMs = 5000 };

        // ✅ zone terrain (4 points)
        private readonly CourtArea _court = new CourtArea();

        private readonly OverlayRenderer _overlays = new OverlayRenderer { ManualBoxHalf = 12 };
        private readonly SnapshotBuffer _snapshots = new SnapshotBuffer();

        private InputController? _input;
        private HudPresenter? _hud;
        private CameraPipeline? _pipeline;

        private CancellationTokenSource? _cts;
        private Task? _task;

        private readonly string _snapDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RealSense_Captures");

        private const string HelpText =
            "Touches: Click=Tracker manuel | Ctrl+Click=Ligne | Shift+Click=Calibrer balle | Alt+Click=Terrain(4) | C=Reset terrain | A=Auto ON/OFF | R=Reset ligne ";

        private bool _ballSelected = false;

        public CameraView()
        {
            InitializeComponent();

            _autoFollower = new AutoTemplateFollower(_ballDetector, _autoTracker)
            {
                RoiHalfSize = 240,
                ReacquireEveryNFrames = 2,
                ReacquireEveryNFramesWhenUnknown = 1,
                MinConfirmFrames = 2,
                VerifyEveryNFrames = 3,
                MaxDriftPx = 30f
            };

            cameraPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            cameraPictureBox.BackColor = Color.Black;

            Directory.CreateDirectory(_snapDir);

            _input = new InputController(cameraPictureBox, _snapshots);

            _hud = new HudPresenter(distanceLabel, traitementFrameLabel);
            _hud.SetUiHz(10);

            // ✅ ctor pipeline avec _court
            _pipeline = new CameraPipeline(
                _camera,
                _tracker,
                _lineDetector, _lineLock,
                _autoTracker, _autoFollower,
                _traj,
                _impact,
                _ground,
                _inOutLatch,
                _court);

            cameraPictureBox.MouseClick += CameraPictureBox_MouseClick;

            button1.Text = "Prendre photo";
            button1.Click += button1_Click;

            KeyPreview = true;
            KeyDown += CameraView_KeyDown;

            distanceLabel.ForeColor = Color.Black;
            distanceLabel.Text = HelpText;
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

                _ballSelected = false;
                distanceLabel.ForeColor = Color.Black;
                distanceLabel.Text = HelpText;

                _camera.Start(640, 480, 30);

                _cts = new CancellationTokenSource();
                _task = Task.Run(() => Loop(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                SafeUI(() =>
                {
                    distanceLabel.ForeColor = Color.OrangeRed;
                    distanceLabel.Text = "Start error: " + ex.Message;
                });
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

                _snapshots.Clear();

                SafeUI(() =>
                {
                    var old = cameraPictureBox.Image;
                    cameraPictureBox.Image = null;
                    old?.Dispose();

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
                var pipeline = _pipeline;
                if (pipeline == null)
                {
                    Thread.Sleep(20);
                    continue;
                }

                FrameResult r;
                try
                {
                    r = pipeline.ProcessOneFrame(_overlays);
                    if (!r.HasFrame) continue;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Loop error: " + ex);
                    Thread.Sleep(10);
                    continue;
                }

                SafeUI(() =>
                {
                    if (r.BitmapToShow != null)
                    {
                        var old = cameraPictureBox.Image;
                        cameraPictureBox.Image = r.BitmapToShow;
                        old?.Dispose();

                        _snapshots.Update(r.BitmapToShow);
                    }

                    if (!r.ManualTrackingOk)
                        _hud?.ShowTempMessage(r.NowTicks, "Objet perdu (reclique).", Color.OrangeRed);

                    _hud?.RenderHelpOrDistance(
                        r.NowTicks,
                        HelpText,
                        showDistance: _ballSelected,
                        rawDepth: r.RawDepth,
                        depthUnits: r.DepthUnits,
                        latch: r.Latch
                    );

                    _hud?.UpdateFrameTime(r.FrameMs);
                });
            }
        }

        private void CameraPictureBox_MouseClick(object? sender, MouseEventArgs e)
        {
            if (_input == null) return;

            // ✅ Alt+Click => terrain
            if ((ModifierKeys & Keys.Alt) == Keys.Alt) { AddCourtPointFromClick(e.Location); return; }

            if ((ModifierKeys & Keys.Shift) == Keys.Shift) { CalibrateBallColorFromClick(e.Location); return; }
            if ((ModifierKeys & Keys.Control) == Keys.Control) { AddLinePointFromClick(e.Location); return; }

            StartTrackingFromClick(e.Location);
        }

        private void StartTrackingFromClick(Point clickLocation)
        {
            if (_input == null) return;

            if (!_input.TryGetClickPixel(clickLocation, out int x, out int y))
            {
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Image pas prête / clique dans l'image.", Color.Black);
                return;
            }

            if (!_camera.TryGetAlignedFrames(500, out var rgb, out _))
            {
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Frame non dispo.", Color.Black);
                return;
            }

            if (!_tracker.TryStart(rgb, _camera.ColorW, _camera.ColorH, x, y))
            {
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Impossible de créer template.", Color.Black);
                return;
            }

            _ballSelected = true;
            _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Balle sélectionnée ✅", Color.Black, 900);
        }

        private void CalibrateBallColorFromClick(Point clickLocation)
        {
            if (_input == null) return;

            if (!_input.TryGetClickPixel(clickLocation, out int x, out int y))
            {
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Image pas prête / Shift+clique dans l'image.", Color.Black);
                return;
            }

            using var img = _snapshots.TryClone();
            if (img == null)
            {
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Image pas prête pour calibration.", Color.Black);
                return;
            }

            Color c = img.GetPixel(x, y);

            _ballDetector.TargetR = c.R;
            _ballDetector.TargetG = c.G;
            _ballDetector.TargetB = c.B;

            _ballDetector.TolR = 100;
            _ballDetector.TolG = 100;
            _ballDetector.TolB = 100;
            _ballDetector.MinBlobPixels = 60;

            _pipeline?.ResetAllStates();
            _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, $"Calibration balle: R{c.R} G{c.G} B{c.B}", Color.Black);
        }

        private void AddLinePointFromClick(Point clickLocation)
        {
            if (_input == null) return;

            if (!_input.TryGetClickPixel(clickLocation, out int x, out int y))
            {
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Image pas prête / Ctrl+clique dans l'image.", Color.Black);
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

            _hud?.ShowTempMessage(DateTime.UtcNow.Ticks,
                hasLineNow ? "✅ Ligne détectée (Ctrl+Click ajouter / R reset)" : $"Mode ligne: {count}/{_lineDetector.MinPointsToFit} points",
                Color.Black);

            _pipeline?.ResetLineRelatedStates();
        }

        private void AddCourtPointFromClick(Point clickLocation)
        {
            if (_input == null) return;

            if (!_input.TryGetClickPixel(clickLocation, out int x, out int y))
            {
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Image pas prête / Alt+clique dans l'image.", Color.Black);
                return;
            }

            _court.AddPoint(new PointF(x, y));

            if (_court.HasCourt)
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "✅ Terrain défini (4 points).", Color.Black);
            else
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, $"Terrain: {_court.Points.Count}/4 (Alt+Click)", Color.Black);
        }

        private void CameraView_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.R)
            {
                lock (_lineLock) _lineDetector.Clear();
                _pipeline?.ResetLineRelatedStates();
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Ligne reset ✅ (Ctrl+Click)", Color.Black);
            }
            else if (e.KeyCode == Keys.A)
            {
                if (_pipeline != null)
                {
                    _pipeline.AutoEnabled = !_pipeline.AutoEnabled;
                    _pipeline.ResetAllStates();
                    _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "AUTO BALL: " + (_pipeline.AutoEnabled ? "ON" : "OFF"), Color.Black);
                }
            }
            else if (e.KeyCode == Keys.C)
            {
                _court.Clear();
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Terrain reset ✅ (Alt+Click x4)", Color.Black);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                using var snap = _snapshots.TryClone();
                if (snap == null)
                {
                    _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Pas d'image à enregistrer.", Color.Black);
                    return;
                }

                string fileName = $"rs_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                string fullPath = Path.Combine(_snapDir, fileName);

                snap.Save(fullPath, ImageFormat.Png);
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, $"Photo enregistrée: {fileName}", Color.Black);
            }
            catch (Exception ex)
            {
                _hud?.ShowTempMessage(DateTime.UtcNow.Ticks, "Erreur photo: " + ex.Message, Color.OrangeRed);
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
    }
}