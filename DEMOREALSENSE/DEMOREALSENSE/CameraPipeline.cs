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

        // état interne air/sol
        private bool _wasClearlyInAir = false;

        // affichage croix (persist)
        private PointF? _impactMark = null;
        private long _impactMarkTicks = 0;
        private const int ImpactMarkMs = 2500;

        public bool AutoEnabled { get; set; } = true;

        public CameraPipeline(
            RealSenseCameraService camera,
            TemplateTracker manualTracker,
            ClickLineDetector lineDetector, object lineLock,
            BallDetector ballDetector,
            TemplateTracker autoTracker, AutoTemplateFollower autoFollower,
            TrajectoryTracker traj, ImpactDetector impact,
            GroundEstimator ground, InOutLatch latch)
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
            _latch.Reset();

            _wasClearlyInAir = false;
            _impactMark = null;
            _impactMarkTicks = 0;
        }

        public FrameResult ProcessOneFrame(OverlayRenderer overlays)
        {
            var sw = Stopwatch.StartNew();
            var res = new FrameResult();

            long nowTicks = DateTime.UtcNow.Ticks;
            res.NowTicks = nowTicks;

            // 1) frames
            if (!_camera.TryGetAlignedFrames(2000, out var rgb, out var depthU16))
            {
                res.HasFrame = false;
                return res;
            }

            res.HasFrame = true;
            res.DepthUnits = _camera.DepthUnits;

            int w = _camera.ColorW;
            int h = _camera.ColorH;

            // 2) manuel update (sans casser)
            res.ManualTrackingOk = true;
            if (_manualTracker.IsTracking)
            {
                bool ok = _manualTracker.TryUpdate(rgb, w, h);
                res.ManualTrackingOk = ok;
                if (!ok) _manualTracker.Stop();
            }

            // 3) bitmap
            using var bmp = FrameBitmapConverter.RgbToBitmap24bpp(rgb, w, h);

            // 4) overlay manuel
            if (_manualTracker.IsTracking && _manualTracker.X >= 0 && _manualTracker.Y >= 0)
                overlays.DrawManualTracker(bmp, _manualTracker.X, _manualTracker.Y);

            // 5) AUTO ball
            int bx = -1, by = -1;
            bool hasAutoBall = false;

            if (AutoEnabled)
            {
                hasAutoBall = _autoFollower.TryUpdate(rgb, w, h, bmp, out bx, out by);
                if (hasAutoBall && bx >= 0 && by >= 0)
                    overlays.DrawAutoBall(bmp, bx, by, 12);
            }

            // 6) quelle balle on juge ? (manuel prioritaire)
            bool useManual = _manualTracker.IsTracking && _manualTracker.X >= 0 && _manualTracker.Y >= 0;
            if (useManual)
            {
                res.Source = "MANUAL";
                res.HasBall = true;
                res.BallX = _manualTracker.X;
                res.BallY = _manualTracker.Y;
            }
            else if (hasAutoBall && bx >= 0 && by >= 0)
            {
                res.Source = "AUTO";
                res.HasBall = true;
                res.BallX = bx;
                res.BallY = by;
            }
            else
            {
                res.Source = "NONE";
                res.HasBall = false;
            }

            // 7) ligne overlay
            overlays.DrawLine(bmp, _lineDetector, _lineLock);

            // 8) logique (distance, latch, impact)
            if (res.HasBall)
            {
                // distance
                res.RawDepth = DistanceCalculator.MedianDepthRaw(
                    depthU16, _camera.DepthW, _camera.DepthH,
                    res.BallX, res.BallY, radius: 2);

                // IN/OUT (continue) sur la position courante (manuel OU auto)
                if (InOutJudge.TryIsIn(_lineDetector, _lineLock, new PointF(res.BallX, res.BallY), out bool isInNow))
                    _latch.Update(isInNow, nowTicks);

                // sol depuis la ligne
                if (_ground.TryGetGroundY(_lineDetector, _lineLock, res.BallX, out float yGround))
                {
                    res.HasGround = true;
                    res.GroundY = yGround;

                    // debug sol (petit point)
                    overlays.DrawGroundDebug(bmp, res.BallX, yGround);

                    // trajectoire seulement si on a une balle
                    _traj.Add(new PointF(res.BallX, res.BallY), nowTicks);

                    // air/sol
                    bool clearlyInAir = _ground.IsClearlyInAir(_lineDetector, _lineLock, new PointF(res.BallX, res.BallY), out _);
                    bool nearGround = Math.Abs(res.BallY - yGround) <= _ground.NearGroundPx;

                    // impact = uniquement si proche sol ET gros changement trajectoire
                    if (_traj.TryGetPreviousVelocity(out var vPrev) && _traj.TryGetVelocity(out var vNow))
                    {
                        // condition forte : air -> proche sol (super fiable)
                        bool airToGround = _wasClearlyInAir && nearGround;

                        // condition cinématique : changement sec (vyFlip/angleBreak)
                        bool sharp = _impact.TryDetectImpact(nowTicks, vPrev, vNow);

                        bool impactSol = nearGround && (airToGround || sharp);

                        if (impactSol)
                        {
                            // croix EXACTEMENT sur le sol
                            _impactMark = new PointF(res.BallX, yGround);
                            _impactMarkTicks = nowTicks;
                        }
                    }

                    _wasClearlyInAir = clearlyInAir;
                }
            }

            // 9) dessiner croix (si encore visible)
            if (_impactMark.HasValue)
            {
                long dt = nowTicks - _impactMarkTicks;
                if (dt <= TimeSpan.FromMilliseconds(ImpactMarkMs).Ticks)
                {
                    overlays.DrawImpactCross(bmp, _impactMark.Value.X, _impactMark.Value.Y);
                    res.HasImpact = true;
                    res.ImpactPoint = _impactMark.Value;
                }
                else
                {
                    _impactMark = null;
                }
            }

            // 10) output bitmap UI
            res.BitmapToShow = (Bitmap)bmp.Clone();
            res.Latch = _latch;

            sw.Stop();
            res.FrameMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }
    }
}