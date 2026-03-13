using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Juge IN/OUT avec 3 états : In, Out, OnLine.
    ///
    /// RÈGLES :
    ///   - OnLine  : balle dans la zone ±LineWidthPx autour de la ligne → compte comme IN, PAS de croix
    ///   - In      : balle clairement du côté IN (au-delà de LineWidthPx)
    ///   - Out     : balle clairement du côté OUT (au-delà de LineWidthPx)
    ///
    /// LineWidthPx doit correspondre à la largeur visuelle de ta ligne sur la table.
    /// Sur une table de pickleball, une ligne fait ~5-8px à 640px. Mets 8-12px pour avoir de la marge.
    /// </summary>
    public static class InOutJudge
    {
        public enum Zone { In, Out, OnLine }

        /// <summary>
        /// Retourne la zone précise (In / Out / OnLine).
        /// OnLine = IN selon les règles du sport (la balle touchant la ligne est IN).
        /// </summary>
        public static bool TryGetZone(
            ClickLineDetector lineDet, object lineLock,
            PointF p,
            out Zone zone,
            float lineWidthPx = 10f)
        {
            zone = Zone.In;

            ClickLineDetector.LineModel line;
            lock (lineLock)
            {
                if (!lineDet.HasLine) return false;
                line = lineDet.Line;
            }

            float cross = ComputeCross(line, p);

            if (cross >= -lineWidthPx && cross <= lineWidthPx)
                zone = Zone.OnLine;
            else if (cross > lineWidthPx)
                zone = Zone.In;
            else
                zone = Zone.Out;

            return true;
        }

        /// <summary>
        /// API simplifiée : retourne isIn=true si In OU OnLine (règle sport).
        /// Compatible avec l'ancienne signature.
        /// </summary>
        public static bool TryIsIn(
            ClickLineDetector lineDet, object lineLock,
            PointF p, out bool isIn,
            float epsilonPx = 10f)
        {
            isIn = true;
            if (!TryGetZone(lineDet, lineLock, p, out Zone zone, epsilonPx))
                return false;

            isIn = (zone != Zone.Out);
            return true;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        public static float ComputeCross(ClickLineDetector.LineModel line, PointF p)
        {
            float dx = line.Direction.X;
            float dy = line.Direction.Y;

            // Convention stable : direction toujours "vers le haut" en image
            if (dy > 0f) { dx = -dx; dy = -dy; }

            float vx = p.X - line.Point.X;
            float vy = p.Y - line.Point.Y;

            return vx * dy - vy * dx;
        }

        // Compat ancienne signature sans epsilon
        public static bool TryIsIn(ClickLineDetector lineDet, object lineLock, PointF p, out bool isIn)
            => TryIsIn(lineDet, lineLock, p, out isIn, 10f);
    }
}