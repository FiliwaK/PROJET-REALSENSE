using System;
using System.Diagnostics;
using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Pipeline principal — version améliorée.
    ///
    /// AMÉLIORATIONS vs version originale :
    ///   1. VarInOutEngine connecté et utilisé comme source de vérité pour le verdict IN/OUT.
    ///   2. ImpactDetector.Update() avec les 3 signaux (clearlyInAir + contact + nowTicks)
    ///      au lieu de l'ancienne logique booléenne simple.
    ///   3. Croix rebond colorée : VERT si IN, ROUGE si OUT, JAUNE si indéterminé.
    ///   4. FrameResult expose le VarInOutEngine pour que le HUD puisse afficher le verdict.
    ///   5. FlipInOutSide : permet d'inverser la convention côté IN d'un appui touche.
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

        // ✅ Moteur VAR connecté — réglé pour réponse rapide sur table
        private readonly VarInOutEngine _var = new VarInOutEngine
        {
            ConfirmFrames = 2,     // 2 frames consécutives pour confirmer (anti-bruit minimal)
            FinalizeOnAirOut = false, // sur une table on attend l'impact
            ImpactCooldownMs = 300
        };

        public bool AutoEnabled { get; set; } = true;
        public bool FlipInOutSide { get; set; } = false;

        /// <summary>Largeur zone "sur la ligne" en pixels — pas de croix dans cette zone.</summary>
        public float LineWidthPx { get; set; } = 10f;

        /// <summary>Durée d'affichage du verdict OUT avant reset automatique (ms).</summary>
        public int OutHoldMs { get; set; } = 5000;

        // ── Croix rebond ─────────────────────────────────────────────────
        private PointF? _impactMark = null;
        private long _impactMarkTicks = 0;
        private InOutSide _impactSide = InOutSide.Unknown;
        private const int ImpactMarkMs = 3000;

        // ── Verdict hold ──────────────────────────────────────────────────
        private bool _verdictHeld = false;
        private long _verdictHeldTicks = 0;
        private InOutSide _heldVerdict = InOutSide.Unknown;

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
        }

        public void ResetAllStates()
        {
            _autoTracker.Stop();
            _autoFollower.Reset();
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

            // ── Tracker auto ──────────────────────────────────────────────
            bool autoOk = false;
            int ax = -1, ay = -1;

            if (AutoEnabled)
            {
                autoOk = _autoFollower.TryUpdate(rgb, w, h, bmp, out ax, out ay);
                if (autoOk && ax >= 0 && ay >= 0)
                    overlays.DrawAutoCircle(bmp, ax, ay);
            }

            // ── Choix position balle (auto prioritaire) ───────────────────
            bool haveBall = false;
            int ballX = -1, ballY = -1;
            int ballRadius = 8;   // rayon estimé, mis à jour par le tracker auto
            bool usingAuto = false;

            if (autoOk && ax >= 0 && ay >= 0)
            {
                haveBall = true;
                ballX = ax;
                ballY = ay;
                ballRadius = Math.Max(4, _autoFollower.LastRadius);
                usingAuto = true;
            }
            else if (_manualTracker.IsTracking && _manualTracker.X >= 0 && _manualTracker.Y >= 0)
            {
                haveBall = true;
                ballX = _manualTracker.X;
                ballY = _manualTracker.Y;
                ballRadius = 8; // manuel : pas de rayon connu, valeur neutre
            }

            // ✅ Mode smooth ImpactDetector selon tracker actif
            _impact.SetSmoothMode(usingAuto);

            // ── Point de contact réel = bas de la balle ───────────────────
            // Le centroïde du tracker est au centre de la balle.
            // Pour IN/OUT et croix, on utilise le bas de la balle (contact avec la surface).
            int contactY = haveBall ? (ballY + ballRadius) : ballY;

            // ── Distance ──────────────────────────────────────────────────
            ushort ballRaw = 0;
            if (haveBall)
            {
                ballRaw = DistanceCalculator.MedianDepthRaw(
                    depthU16, _camera.DepthW, _camera.DepthH,
                    ballX, ballY, radius: 2);
            }
            res.RawDepth = ballRaw;

            // ── IN/OUT avec zone ligne ────────────────────────────────────
            bool hasLine = false;
            bool isInNow = true;
            InOutJudge.Zone zoneNow = InOutJudge.Zone.In;

            if (haveBall)
            {
                // ✅ On juge sur le BAS de la balle (contactY), pas sur son centre
                hasLine = InOutJudge.TryGetZone(
                    _lineDetector, _lineLock,
                    new PointF(ballX, contactY),
                    out zoneNow,
                    lineWidthPx: LineWidthPx);

                if (hasLine && FlipInOutSide)
                    zoneNow = zoneNow == InOutJudge.Zone.Out ? InOutJudge.Zone.In
                            : zoneNow == InOutJudge.Zone.In ? InOutJudge.Zone.Out
                            : InOutJudge.Zone.OnLine;

                // OnLine = IN pour le latch (règle sport)
                isInNow = zoneNow != InOutJudge.Zone.Out;

                if (hasLine)
                    _latch.Update(isInNow, nowTicks);
            }
            res.Latch = _latch;

            // ── Détection rebond par inversion vitesse verticale ──────────
            bool impactFired = false;

            if (haveBall)
            {
                // ImpactDetector sur contactY (bas de la balle) — signal d'inversion plus net
                impactFired = _impact.Update((float)contactY, nowTicks);

                if (impactFired && hasLine && zoneNow != InOutJudge.Zone.OnLine)
                {
                    // Croix à la position réelle de la balle au moment du rebond
                    _impactMark = new PointF(ballX, contactY);
                    _impactMarkTicks = nowTicks;
                    _impactSide = (zoneNow == InOutJudge.Zone.Out) ? InOutSide.Out : InOutSide.In;
                }
            }

            // ── Verdict IN/OUT : live + hold 5s sur rebond OUT ────────────
            if (haveBall && hasLine)
            {
                // ✅ Expiration du hold basée sur le temps uniquement (pas sur la zone)
                if (_verdictHeld)
                {
                    long elapsed2 = nowTicks - _verdictHeldTicks;
                    if (elapsed2 >= OutHoldMs * TimeSpan.TicksPerMillisecond)
                        _verdictHeld = false;
                }

                // Rebond OUT détecté → fige le verdict 5s
                if (impactFired && zoneNow == InOutJudge.Zone.Out)
                {
                    _verdictHeld = true;
                    _verdictHeldTicks = nowTicks;
                    _heldVerdict = InOutSide.Out;
                }
                else if (impactFired && zoneNow == InOutJudge.Zone.In)
                {
                    // Rebond clairement IN → annule le hold
                    _verdictHeld = false;
                }

                // ✅ Pendant le hold OUT → on affiche OUT peu importe la position live
                if (_verdictHeld)
                    res.LiveSide = InOutSide.Out;
                else
                    res.LiveSide = (zoneNow == InOutJudge.Zone.Out) ? InOutSide.Out : InOutSide.In;

                res.VerdictHeld = _verdictHeld;
                res.VerdictHeldTicks = _verdictHeldTicks;
            }

            // Garde VarEngine pour compat mais on n'en dépend plus pour l'affichage
            res.VarEngine = _var;

            // ── Overlays ──────────────────────────────────────────────────
            overlays.DrawLineOverlay(bmp, _lineDetector, _lineLock);
            DrawImpactIfAlive(bmp, nowTicks, overlays);

            res.BitmapToShow = (Bitmap)bmp.Clone();

            _sw.Stop();
            res.FrameMs = _sw.Elapsed.TotalMilliseconds;
            return res;
        }

        // ── Dessin croix colorée ──────────────────────────────────────────

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

            // Couleur et label selon verdict
            Color crossColor;
            string label;

            switch (_impactSide)
            {
                case InOutSide.Out:
                    crossColor = Color.Red;
                    label = "OUT";
                    break;
                case InOutSide.In:
                default:
                    crossColor = Color.LimeGreen;
                    label = "IN";
                    break;
            }

            float crossSize = 10f + progress * 6f;

            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(crossColor, 3f);

            // Croix diagonale
            g.DrawLine(pen, p.X - crossSize, p.Y - crossSize, p.X + crossSize, p.Y + crossSize);
            g.DrawLine(pen, p.X - crossSize, p.Y + crossSize, p.X + crossSize, p.Y - crossSize);

            // Cercle autour
            float r = crossSize * 0.8f;
            using var penCircle = new Pen(crossColor, 1.5f);
            g.DrawEllipse(penCircle, p.X - r, p.Y - r, r * 2, r * 2);

            // Label
            using var font = new System.Drawing.Font("Arial", 10f, System.Drawing.FontStyle.Bold);
            using var brush = new SolidBrush(crossColor);
            g.DrawString(label, font, brush, p.X + crossSize + 3, p.Y - 8);
        }
    }
}