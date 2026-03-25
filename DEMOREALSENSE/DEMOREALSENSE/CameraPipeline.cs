using System;
using System.Diagnostics;
using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Pipeline principal.
    ///
    /// CORRECTIFS :
    ///   - Ligne 3D dessinée SEULEMENT quand le plan est prêt (IsReady).
    ///     Avant : fallback 2D classique.
    ///   - API DrawLineOverlay3D utilise LineRealWidthMeters (en mètres)
    ///     au lieu de LineWidthPx pour le calcul de largeur perspective.
    ///   - Rebond : contactY + MinDeltaPx=10.
    ///   - LineWidthPx conservé pour InOutJudge (zone sur la ligne).
    /// </summary>
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

        // ── Plan de table 3D ──────────────────────────────────────────────
        public readonly TablePlaneDetector TablePlane = new TablePlaneDetector
        {
            ScanBottomFraction = 0.55f,
            ScanTopSkipFraction = 0.10f,
            SampleStep = 12,
            RansacIterations = 80,
            RansacInlierM = 0.015f,
            MinInliers = 40,
            UpdateIntervalMs = 2000
        };

        private readonly VarInOutEngine _var = new VarInOutEngine
        {
            ConfirmFrames = 2,
            FinalizeOnAirOut = false,
            ImpactCooldownMs = 300
        };

        private IDetectionStrategy? _strategy;

        public void SetDetectionStrategy(IDetectionStrategy? strategy)
        {
            _strategy?.Reset();
            _strategy = strategy;
            _strategy?.Reset();

            if (strategy == null || strategy is AlgoDetectionStrategy)
                lock (_lineLock) _lineDetector.Clear();

            ResetLineRelatedStates();
        }

        public bool AutoEnabled { get; set; } = true;
        public bool FlipInOutSide { get; set; } = false;

        /// <summary>
        /// Largeur réelle de la ligne de jeu en mètres.
        /// 0.025f = 2.5 cm. Ajuster selon ton ruban.
        /// </summary>
        public float LineRealWidthMeters { get; set; } = 0.025f;

        /// <summary>
        /// Largeur de la ligne en pixels pour InOutJudge.
        /// Calculée automatiquement depuis la profondeur.
        /// </summary>
        public float LineWidthPx { get; set; } = 6f;

        public int OutHoldMs { get; set; } = 5000;

        // ── Croix rebond ──────────────────────────────────────────────────
        private PointF? _impactMark = null;
        private long _impactMarkTicks = 0;
        private InOutSide _impactSide = InOutSide.Unknown;
        private const int ImpactMarkMs = 3000;

        // ── Verdict hold ──────────────────────────────────────────────────
        private bool _verdictHeld = false;
        private long _verdictHeldTicks = 0;
        private InOutSide _heldVerdict = InOutSide.Unknown;

        // ── LineWidthPx mémorisée ──────────────────────────────────────────
        private float _computedLineWidthPx = -1f;

        private readonly Stopwatch _sw = new Stopwatch();

        // ─────────────────────────────────────────────────────────────────

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

        // ── Reset ─────────────────────────────────────────────────────────

        public void ResetLineRelatedStates()
        {
            _traj.Reset();
            _impact.Reset();
            _latch.Reset();
            _var.Reset();

            _impactMark = null;
            _impactMarkTicks = 0;
            _impactSide = InOutSide.Unknown;

            _verdictHeld = false;
            _verdictHeldTicks = 0;
            _heldVerdict = InOutSide.Unknown;

            _computedLineWidthPx = -1f;
        }

        public void ResetAllStates()
        {
            _autoTracker.Stop();
            _autoFollower.Reset();
            _strategy?.Reset();
            TablePlane.Reset();
            ResetLineRelatedStates();
        }

        // ── Traitement d'une frame ────────────────────────────────────────

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

            // ── Détection plan de table (toutes les 2s) ───────────────────
            TablePlane.TryUpdate(depthU16, w, h, _camera.DepthUnits, nowTicks);
            res.TablePlaneReady = TablePlane.IsReady;

            // ── Tracker manuel ────────────────────────────────────────────
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

            // ── Stratégie IA ou algo ──────────────────────────────────────
            bool autoOk = false;
            int ax = -1, ay = -1;

            if (_strategy != null)
            {
                var det = _strategy.Detect(rgb, bmp, w, h);
                if (det.BallCenter.HasValue)
                {
                    autoOk = true;
                    ax = (int)det.BallCenter.Value.X;
                    ay = (int)det.BallCenter.Value.Y;
                    overlays.DrawIaCircle(bmp, ax, ay,
                        Math.Max(12, (int)(_autoFollower.LastRadius * 1.2f)));

                    if (det.HasIaLine)
                        lock (_lineLock) _lineDetector.SetLineModel(det.IaLineModel!.Value);
                }
            }
            else if (AutoEnabled)
            {
                autoOk = _autoFollower.TryUpdate(rgb, w, h, bmp, out ax, out ay);
            }

            if (autoOk && ax >= 0 && ay >= 0 && _strategy == null)
                overlays.DrawAutoCircle(bmp, ax, ay);

            // ── Position balle ────────────────────────────────────────────
            bool haveBall = false;
            int ballX = -1, ballY = -1;
            int ballRadius = 8;
            bool usingAuto = false;

            if (autoOk && ax >= 0 && ay >= 0)
            {
                haveBall = true;
                ballX = ax; ballY = ay;
                ballRadius = Math.Max(4, _autoFollower.LastRadius);
                usingAuto = true;
            }
            else if (_manualTracker.IsTracking && _manualTracker.X >= 0 && _manualTracker.Y >= 0)
            {
                haveBall = true;
                ballX = _manualTracker.X;
                ballY = _manualTracker.Y;
                ballRadius = 8;
            }

            _impact.SetSmoothMode(usingAuto);

            int contactY = haveBall ? (ballY + ballRadius) : ballY;

            // ── Profondeur balle ──────────────────────────────────────────
            ushort ballRaw = 0;
            if (haveBall)
            {
                ballRaw = DistanceCalculator.MedianDepthRaw(
                    depthU16, _camera.DepthW, _camera.DepthH,
                    ballX, ballY, radius: 2);
            }
            res.RawDepth = ballRaw;
            float ballDepthM = ballRaw == 0 ? 0f : ballRaw * _camera.DepthUnits;

            // ── LineWidthPx pour InOutJudge (calculé depuis profondeur) ───
            if (_computedLineWidthPx < 0 && ballRaw != 0)
            {
                float distM = ballDepthM;
                if (distM > 0.1f && distM < 5f)
                {
                    float widthAtDist = 2f * distM * MathF.Tan(TablePlaneDetector.HFovRad / 2f);
                    float pxPerMeter = w / widthAtDist;
                    _computedLineWidthPx = MathF.Max(3f, LineRealWidthMeters * pxPerMeter);
                    LineWidthPx = _computedLineWidthPx;
                }
            }

            // ── IN/OUT ────────────────────────────────────────────────────
            bool hasLine = false;
            bool isInNow = true;
            InOutJudge.Zone zoneNow = InOutJudge.Zone.In;

            if (haveBall)
            {
                hasLine = InOutJudge.TryGetZone(
                    _lineDetector, _lineLock,
                    new PointF(ballX, contactY),
                    out zoneNow,
                    lineWidthPx: LineWidthPx);

                if (hasLine && FlipInOutSide)
                    zoneNow = zoneNow == InOutJudge.Zone.Out ? InOutJudge.Zone.In
                            : zoneNow == InOutJudge.Zone.In ? InOutJudge.Zone.Out
                            : InOutJudge.Zone.OnLine;

                isInNow = zoneNow != InOutJudge.Zone.Out;
                if (hasLine) _latch.Update(isInNow, nowTicks);
            }
            res.Latch = _latch;

            // ── Y surface réel pour la croix rebond ───────────────────────
            bool hasGroundY = _ground.TryGetGroundY(
                _lineDetector, _lineLock, ballX, out float groundY,
                TablePlane.IsReady ? TablePlane : null,
                w, h, ballDepthM, depthU16, _camera.DepthUnits);
            if (!hasGroundY) groundY = contactY;

            // ── Détection rebond ──────────────────────────────────────────
            bool impactFired = false;

            if (haveBall)
            {
                impactFired = _impact.UpdateBounce((float)ballX, (float)contactY, nowTicks);

                if (impactFired)
                {
                    float crossY = hasGroundY ? groundY : (float)contactY;
                    _impactMark = new PointF(ballX, crossY);
                    _impactMarkTicks = nowTicks;
                    _impactSide = hasLine
                        ? (zoneNow == InOutJudge.Zone.Out ? InOutSide.Out : InOutSide.In)
                        : InOutSide.Unknown;
                }
            }

            // ── Verdict IN/OUT avec hold 5s ───────────────────────────────
            if (haveBall && hasLine)
            {
                if (_verdictHeld)
                {
                    long elapsed2 = nowTicks - _verdictHeldTicks;
                    if (elapsed2 >= OutHoldMs * TimeSpan.TicksPerMillisecond)
                        _verdictHeld = false;
                }

                if (impactFired && zoneNow == InOutJudge.Zone.Out)
                {
                    _verdictHeld = true;
                    _verdictHeldTicks = nowTicks;
                    _heldVerdict = InOutSide.Out;
                }
                else if (impactFired && zoneNow == InOutJudge.Zone.In)
                {
                    _verdictHeld = false;
                }

                res.LiveSide = _verdictHeld
                    ? InOutSide.Out
                    : (zoneNow == InOutJudge.Zone.Out ? InOutSide.Out : InOutSide.In);

                res.VerdictHeld = _verdictHeld;
                res.VerdictHeldTicks = _verdictHeldTicks;
            }

            res.VarEngine = _var;

            // ── Overlays ──────────────────────────────────────────────────
            // La ligne 3D n'est dessinée QUE si le plan est prêt.
            // Avant : fallback 2D automatique dans DrawLineOverlay3D.
            overlays.DrawLineOverlay3D(
                bmp,
                _lineDetector, _lineLock,
                LineRealWidthMeters,
                TablePlane.IsReady ? TablePlane : null,
                TablePlane.IsReady ? depthU16 : null,
                _camera.DepthUnits);

            DrawImpactIfAlive(bmp, nowTicks, overlays);

            res.BitmapToShow = (Bitmap)bmp.Clone();

            _sw.Stop();
            res.FrameMs = _sw.Elapsed.TotalMilliseconds;
            return res;
        }

        // ── Dessin croix rebond ───────────────────────────────────────────

        private void DrawImpactIfAlive(Bitmap bmp, long nowTicks, OverlayRenderer overlays)
        {
            if (!_impactMark.HasValue) return;

            long elapsed = nowTicks - _impactMarkTicks;
            if (elapsed > ImpactMarkMs * TimeSpan.TicksPerMillisecond)
            {
                _impactMark = null;
                return;
            }

            var p = _impactMark.Value;
            float progress = 1f - (float)(elapsed / (double)(ImpactMarkMs * TimeSpan.TicksPerMillisecond));
            float crossSize = 10f + progress * 6f;

            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.White, 3f);
            using var penCircle = new Pen(Color.White, 1.5f);

            g.DrawLine(pen, p.X - crossSize, p.Y - crossSize, p.X + crossSize, p.Y + crossSize);
            g.DrawLine(pen, p.X - crossSize, p.Y + crossSize, p.X + crossSize, p.Y - crossSize);

            float r = crossSize * 0.8f;
            g.DrawEllipse(penCircle, p.X - r, p.Y - r, r * 2, r * 2);
        }
    }
}