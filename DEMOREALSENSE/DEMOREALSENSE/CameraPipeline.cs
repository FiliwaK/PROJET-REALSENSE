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

        private readonly TemplateTracker _autoTracker;
        private readonly AutoTemplateFollower _autoFollower;

        private readonly TrajectoryTracker _traj;
        private readonly ImpactDetector _impact;
        private readonly GroundEstimator _ground;
        private readonly InOutLatch _latch;

        public bool AutoEnabled { get; set; } = true;

        // croix
        private PointF? _impactMark = null;
        private long _impactMarkTicks = 0;
        private const int ImpactMarkMs = 2500;

        // ✅ garde l’état “en l’air” (clé pour éviter croix sur crossing)
        private bool _wasClearlyInAir = false;

        private readonly Stopwatch _sw = new Stopwatch();

        public CameraPipeline(
            RealSenseCameraService camera,
            TemplateTracker manualTracker,
            ClickLineDetector lineDetector, object lineLock,
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

            _autoTracker = autoTracker;
            _autoFollower = autoFollower;

            _traj = traj;
            _impact = impact;
            _ground = ground;
            _latch = latch;
        }

        public void ResetLineRelatedStates()
        {
            _traj.Reset();
            _impact.Reset();
            _latch.Reset();

            _impactMark = null;
            _impactMarkTicks = 0;
            _wasClearlyInAir = false;
        }

        public void ResetAllStates()
        {
            _autoTracker.Stop();
            _autoFollower.Reset();
            ResetLineRelatedStates();
        }

        public FrameResult ProcessOneFrame(OverlayRenderer overlays)
        {
            var res = new FrameResult();
            _sw.Restart();

            long nowTicks = DateTime.UtcNow.Ticks;
            res.NowTicks = nowTicks;

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

            // ===== MANUEL =====
            res.ManualTrackingOk = true;
            if (_manualTracker.IsTracking)
            {
                bool ok = _manualTracker.TryUpdate(rgb, w, h);
                res.ManualTrackingOk = ok;
                if (!ok) _manualTracker.Stop();
            }

            using var bmp = FrameBitmapConverter.RgbToBitmap24bpp(rgb, w, h);

            if (_manualTracker.IsTracking && _manualTracker.X >= 0 && _manualTracker.Y >= 0)
                overlays.DrawManualBox(bmp, _manualTracker.X, _manualTracker.Y);

            // ===== AUTO =====
            bool autoOk = false;
            int ax = -1, ay = -1;

            if (AutoEnabled)
            {
                autoOk = _autoFollower.TryUpdate(rgb, w, h, bmp, out ax, out ay);
                if (autoOk && ax >= 0 && ay >= 0)
                    overlays.DrawAutoCircle(bmp, ax, ay);
            }

            // ===== choix balle (auto sinon manuel) =====
            bool haveBall = false;
            int ballX = -1, ballY = -1;

            if (autoOk && ax >= 0 && ay >= 0)
            {
                haveBall = true; ballX = ax; ballY = ay;
            }
            else if (_manualTracker.IsTracking && _manualTracker.X >= 0 && _manualTracker.Y >= 0)
            {
                haveBall = true; ballX = _manualTracker.X; ballY = _manualTracker.Y;
            }

            // distance
            ushort ballRaw = 0;
            if (haveBall)
            {
                ballRaw = DistanceCalculator.MedianDepthRaw(
                    depthU16, _camera.DepthW, _camera.DepthH,
                    ballX, ballY, radius: 2);
            }
            res.RawDepth = ballRaw;

            // ===== IN/OUT latch (auto OU manuel) + "sur la ligne=IN" via InOutJudge epsilon =====
            if (haveBall)
            {
                if (InOutJudge.TryIsIn(_lineDetector, _lineLock, new PointF(ballX, ballY), out bool isInNow, epsilonPx: 3f))
                    _latch.Update(isInNow, nowTicks);
            }
            res.Latch = _latch;

            // ===== IMPACT (croix) : uniquement AIR->CONTACT SOL =====
            if (haveBall && _ground.TryGetGroundY(_lineDetector, _lineLock, ballX, out float yGround))
            {
                // contact = proche du sol (pixels)
                bool contact = Math.Abs(ballY - yGround) <= _ground.NearGroundPx;

                // en l’air = nettement au-dessus du sol
                bool clearlyInAir = (yGround - ballY) >= _ground.AboveGroundPx;

                // ✅ impact seulement si on était en l’air avant
                bool airToContact = _wasClearlyInAir && contact;

                if (_impact.UpdateAirToGround(airToContact, nowTicks))
                {
                    _impactMark = new PointF(ballX, yGround);
                    _impactMarkTicks = nowTicks;
                }

                _wasClearlyInAir = clearlyInAir;

                overlays.DrawGroundDebug(bmp, ballX, yGround);
            }
            else
            {
                // pas de ligne/sol -> pas d’impact
                _wasClearlyInAir = false;
            }

            // overlays ligne
            overlays.DrawLineOverlay(bmp, _lineDetector, _lineLock);

            // croix
            DrawImpactIfAlive(bmp, nowTicks);

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
    }
}