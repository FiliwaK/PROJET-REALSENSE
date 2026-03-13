using System.Drawing;

namespace DEMOREALSENSE
{
    public static class InOutJudge
    {
        /// <summary>
        /// Sur la ligne => IN (epsilonPx).
        /// Convention: côté gauche de la direction = IN.
        /// </summary>
        public static bool TryIsIn(ClickLineDetector lineDet, object lineLock, PointF p, out bool isIn, float epsilonPx = 3f)
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

            // stabilise la convention
            if (dy > 0f) { dx = -dx; dy = -dy; }

            float vx = p.X - x0;
            float vy = p.Y - y0;

            float cross = vx * dy - vy * dx;

            // ✅ sur la ligne => IN
            if (cross >= -epsilonPx) isIn = true;
            else isIn = false;

            return true;
        }

        // compat si ton code appelle l’ancienne signature
        public static bool TryIsIn(ClickLineDetector lineDet, object lineLock, PointF p, out bool isIn)
            => TryIsIn(lineDet, lineLock, p, out isIn, 3f);
    }
}