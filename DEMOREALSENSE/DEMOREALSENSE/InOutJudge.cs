using System.Drawing;

namespace DEMOREALSENSE
{
    public static class InOutJudge
    {
        /// <summary>
        /// Retourne true si la ligne existe, et donne isIn selon le côté.
        /// Convention actuelle : "LeftIsIn" (côté gauche de la direction de la ligne = IN).
        /// </summary>
        public static bool TryIsIn(ClickLineDetector lineDet, object lineLock, PointF p, out bool isIn)
        {
            isIn = false;

            ClickLineDetector.LineModel line;
            lock (lineLock)
            {
                if (!lineDet.HasLine) return false;
                line = lineDet.Line;
            }

            float x0 = line.Point.X;
            float y0 = line.Point.Y;
            float dx = line.Direction.X;
            float dy = line.Direction.Y;

            // Normalisation de direction pour garder une convention stable
            // (sinon "IN" peut s'inverser selon l'ordre de click)
            if (dy > 0f) { dx = -dx; dy = -dy; }

            float vx = p.X - x0;
            float vy = p.Y - y0;

            // Produit vectoriel 2D
            float cross = vx * dy - vy * dx;

            isIn = cross > 0f;
            return true;
        }
    }
}