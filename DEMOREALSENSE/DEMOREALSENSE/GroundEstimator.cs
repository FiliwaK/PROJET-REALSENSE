using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Estime la position Y du sol (surface de table) pour un X image donné.
    ///
    /// PRIORITÉ :
    ///   1. TablePlaneDetector (plan 3D RealSense) — colle à la surface réelle
    ///   2. ClickLineDetector (ligne 2D) — fallback si le plan n'est pas encore prêt
    /// </summary>
    public sealed class GroundEstimator
    {
        public float NearGroundPx { get; set; } = 35f;
        public float AboveGroundPx { get; set; } = 80f;
        public float ContactDepthEpsMeters { get; set; } = 0.035f;

        /// <summary>
        /// Calcule yGround au x donné.
        /// Utilise le plan 3D en priorité, puis la ligne 2D en fallback.
        /// </summary>
        public bool TryGetGroundY(
            ClickLineDetector detector, object lineLock,
            int x, out float yGround,
            TablePlaneDetector? plane = null,
            int imgW = 640, int imgH = 480,
            float refDepthM = 0f,
            ushort[]? depth = null, float depthUnits = 0.001f)
        {
            yGround = 0f;

            // ── Priorité 1 : plan 3D ──────────────────────────────────────
            if (plane != null && plane.IsReady)
            {
                // Utilise la profondeur directe si disponible (plus précis)
                if (depth != null && depthUnits > 0)
                {
                    if (plane.TryGetSurfaceYFromDepth(x, imgW, imgH, depth, depthUnits, out float sy1))
                    {
                        yGround = sy1;
                        return true;
                    }
                }
                // Sinon utilise la profondeur de référence de la balle
                if (refDepthM > 0.05f)
                {
                    if (plane.TryGetSurfaceY(x, imgW, imgH, refDepthM, out float sy2))
                    {
                        yGround = sy2;
                        return true;
                    }
                }
            }

            // ── Fallback : ligne 2D ───────────────────────────────────────
            ClickLineDetector.LineModel line;
            lock (lineLock)
            {
                if (!detector.HasLine) return false;
                line = detector.Line;
            }

            float x0 = line.Point.X;
            float y0 = line.Point.Y;
            float dx = line.Direction.X;
            float dy = line.Direction.Y;

            if (Math.Abs(dx) < 1e-6f) { yGround = y0; return true; }

            float t = (x - x0) / dx;
            yGround = y0 + t * dy;
            return true;
        }

        // Surcharge de compatibilité — code existant non cassé
        public bool TryGetGroundY(ClickLineDetector detector, object lineLock,
                                   int x, out float yGround)
            => TryGetGroundY(detector, lineLock, x, out yGround, null, 640, 480, 0f);

        public bool IsClearlyInAir(float y, float yGround)
            => y < (yGround - AboveGroundPx);

        public bool IsContactWithGround(
            int bx, int by, float yGround,
            ushort ballRaw, ushort groundRaw, float depthUnits)
        {
            if (Math.Abs(by - yGround) > NearGroundPx) return false;
            if (ballRaw == 0 || groundRaw == 0) return false;
            float ballM = ballRaw * depthUnits;
            float groundM = groundRaw * depthUnits;
            if (Math.Abs(ballM - groundM) > ContactDepthEpsMeters) return false;
            return true;
        }
    }
}