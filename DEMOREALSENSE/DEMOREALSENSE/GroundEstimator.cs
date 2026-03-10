using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class GroundEstimator
    {
        // En pixels (image)
        public float NearGroundPx { get; set; } = 35f;   // tolérance verticale autour du sol
        public float AboveGroundPx { get; set; } = 80f;  // "clairement en l'air"

        // En mètres : tolérance profondeur balle vs sol pour dire "contact"
        public float ContactDepthEpsMeters { get; set; } = 0.035f; // 3.5 cm (ajuste 0.02..0.06)

        /// <summary>
        /// Calcule yGround (sol) au x donné, en utilisant la ligne détectée.
        /// On suppose que la ligne est tracée AU SOL (ligne de terrain).
        /// </summary>
        public bool TryGetGroundY(ClickLineDetector detector, object lineLock, int x, out float yGround)
        {
            yGround = 0f;

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

            // Si la ligne est presque verticale (dx ~ 0), on ne peut pas estimer y(x) proprement
            if (Math.Abs(dx) < 1e-6f)
            {
                // fallback : utiliser y0
                yGround = y0;
                return true;
            }

            // Ligne param: P = (x0,y0) + t*(dx,dy)
            // On veut x = xTarget => t = (x - x0)/dx
            float t = (x - x0) / dx;
            yGround = y0 + t * dy;
            return true;
        }

        /// <summary>
        /// Renvoie true si le point est clairement en l'air (au-dessus du sol en image)
        /// </summary>
        public bool IsClearlyInAir(float y, float yGround)
            => (y < (yGround - AboveGroundPx));

        /// <summary>
        /// Contact sol robuste :
        /// - la balle est proche en Y du sol (image)
        /// - ET profondeur balle ~ profondeur sol au même x (évite impact "dans l'air")
        /// </summary>
        public bool IsContactWithGround(
            int bx, int by,
            float yGround,
            ushort ballRaw, ushort groundRaw,
            float depthUnits)
        {
            // Gate 1 : proximité verticale
            if (Math.Abs(by - yGround) > NearGroundPx)
                return false;

            if (ballRaw == 0 || groundRaw == 0)
                return false;

            float ballM = ballRaw * depthUnits;
            float groundM = groundRaw * depthUnits;

            // Gate 2 : cohérence profondeur
            if (Math.Abs(ballM - groundM) > ContactDepthEpsMeters)
                return false;

            return true;
        }
    }
}