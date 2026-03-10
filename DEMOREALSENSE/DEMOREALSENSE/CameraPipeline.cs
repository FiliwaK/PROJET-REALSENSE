using System;
using System.Diagnostics;
using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class CameraPipeline
    {
        private readonly RealSenseCameraService _camera;

        private readonly TemplateTracker _manualTracker;
        private readonly ClickLineDetector _lineDetector;
        private readonly object _lineLock;

        private readonly BallDetector _ballDetector;
        private readonly TemplateTracker _autoTracker;
        private readonly AutoTemplateFollower _autoFollower;

        private readonly TrajectoryTracker _traj;
        private readonly ImpactDetector _impact;
        private readonly GroundEstimator _ground;
        private readonly InOutLatch _latch;

        // --- options ---
        public bool AutoEnabled { get; set; } = true;

        // Cross display
        private PointF? _impactMark = null;
        private long _impactMarkTicks = 0;
        private const int ImpactMarkMs = 2500;

        // Air memory
        private bool _wasClearlyInAir = false;

        // Perf
        private readonly Stopwatch _sw = new Stopwatch();

        public CameraPipeline(
            RealSenseCameraService camera,
            TemplateTracker manualTracker,
            ClickLineDetector lineDetector, object lineLock,
            BallDetector ballDetector,
            TemplateTracker autoTracker, AutoTemplateFollower autoFollower,
            TrajectoryTracker traj,
            ImpactDetector impact,
            GroundEstimator ground,
            InOutLatch latch)
        {
            _camera = camera;

            _manualTracker = manualTracker;
            _lineDetector = lineDetector;
            _lineLock = lineLock;

            _ballDetector = ballDetector;
            _autoTracker = autoTracker;
            _autoFollower = autoFollower;

            _traj = traj;
            _impact = impact;
            _ground = ground;
            _latch = latch;
        }

        public void ResetStates()
        {
            _autoTracker.Stop();
            _autoFollower.Reset();

            _traj.Reset();
            _impact.Reset();

            _impactMark = null;
            _impactMarkTicks = 0;

            _wasClearlyInAir = false;
            _latch.Reset();
        }

        public FrameResult ProcessOneFrame(OverlayRenderer overlays)
        {
            var res = new FrameResult();
            _sw.Restart();

            res.NowTicks = DateTime.UtcNow.Ticks;

            // 1) Frames
            if (!_camera.TryGetAlignedFrames(2000, out var rgb, out var depthU16))
            {
                res.HasFrame = false;
                res.FrameMs = _sw.Elapsed.TotalMilliseconds;
                return res;
            }

            res.HasFrame = true;
            res.DepthUnits = _camera.DepthUnits;

            int w = _camera.ColorW;
            int h = _camera.ColorH;

            // 2) Manual tracker update (ne casse pas)
            if (_manualTracker.IsTracking)
            {
                bool ok = _manualTracker.TryUpdate(rgb, w, h);
                res.ManualTrackingOk = ok;
                if (!ok) _manualTracker.Stop();
            }

            // 3) Bitmap
            using var bmp = FrameBitmapConverter.RgbToBitmap24bpp(rgb, w, h);

            // overlays manual
            if (_manualTracker.IsTracking && _manualTracker.X >= 0 && _manualTracker.Y >= 0)
                overlays.DrawManualBox(bmp, _manualTracker.X, _manualTracker.Y);

            // 4) Auto ball
            bool haveAuto = false;
            int bx = -1, by = -1;

            if (AutoEnabled)
            {
                haveAuto = _autoFollower.TryUpdate(rgb, w, h, bmp, out bx, out by);
                if (haveAuto && bx >= 0 && by >= 0)
                    overlays.DrawAutoCircle(bmp, bx, by);
            }

            // 5) Choisir la meilleure source de position de balle (AUTO sinon MANUEL)
            bool haveBall = false;
            int ballX = -1, ballY = -1;

            if (haveAuto && bx >= 0 && by >= 0)
            {
                haveBall = true;
                ballX = bx; ballY = by;
            }
            else if (_manualTracker.IsTracking && _manualTracker.X >= 0 && _manualTracker.Y >= 0)
            {
                haveBall = true;
                ballX = _manualTracker.X; ballY = _manualTracker.Y;
            }

            // 6) Calcul distance balle (si dispo)
            ushort ballRaw = 0;
            if (haveBall)
            {
                ballRaw = DistanceCalculator.MedianDepthRaw(
                    depthU16, _camera.DepthW, _camera.DepthH,
                    ballX, ballY, radius: 2);
            }
            res.RawDepth = ballRaw;

            // 7) IN/OUT latch (AUTO + MANUEL)
            if (haveBall)
            {
                if (InOutJudge.TryIsIn(_lineDetector, _lineLock, new PointF(ballX, ballY), out bool isInNow))
                {
                    _latch.Update(isInNow, res.NowTicks);
                }
            }
            res.Latch = _latch;

            // 8) Impact sol (croix) — UNIQUEMENT contact sol
            if (haveBall)
            {
                if (_ground.TryGetGroundY(_lineDetector, _lineLock, ballX, out float yGround))
                {
                    // profondeur du "sol" au même x (autour de yGround)
                    int gy = Clamp((int)Math.Round(yGround), 0, _camera.DepthH - 1);

                    ushort groundRaw = DistanceCalculator.MedianDepthRaw(
                        depthU16, _camera.DepthW, _camera.DepthH,
                        ballX, gy, radius: 2);

                    // bool air / contact
                    bool clearlyInAir = _ground.IsClearlyInAir(ballY, yGround);

                    bool contact = _ground.IsContactWithGround(
                        ballX, ballY,
                        yGround,
                        ballRaw, groundRaw,
                        _camera.DepthUnits);

                    // On veut surtout : AIR -> CONTACT
                    bool airToContact = (_wasClearlyInAir && contact);

                    // update impact detector (front montant de contact)
                    bool impactNow = false;
                    if (airToContact)
                        impactNow = _impact.Update(contactSol: true, nowTicks: res.NowTicks);
                    else
                        impactNow = _impact.Update(contactSol: contact, nowTicks: res.NowTicks);

                    if (impactNow)
                    {
                        // ✅ Croix au sol (pas à la position de la balle)
                        _impactMark = new PointF(ballX, yGround);
                        _impactMarkTicks = res.NowTicks;
                    }

                    _wasClearlyInAir = clearlyInAir;

                    // debug sol (petit point discret)
                    overlays.DrawGroundDebug(bmp, ballX, yGround);
                }
            }

            // 9) Ligne overlay
            overlays.DrawLineOverlay(bmp, _lineDetector, _lineLock);

            // 10) Croix impact (si active)
            DrawImpactIfAlive(bmp, res.NowTicks);

            // 11) clone pour UI
            res.BitmapToShow = (Bitmap)bmp.Clone();

            _sw.Stop();
            res.FrameMs = _sw.Elapsed.TotalMilliseconds;
            return res;
        }

        private void DrawImpactIfAlive(Bitmap bmp, long nowTicks)
        {
            if (!_impactMark.HasValue) return;

            long dt = nowTicks - _impactMarkTicks;
            if (dt > TimeSpan.FromMilliseconds(ImpactMarkMs).Ticks)
            {
                _impactMark = null;
                return;
            }

            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.Yellow, 3f);

            var p = _impactMark.Value;
            g.DrawLine(pen, p.X - 10, p.Y - 10, p.X + 10, p.Y + 10);
            g.DrawLine(pen, p.X - 10, p.Y + 10, p.X + 10, p.Y - 10);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}