using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    public static class InOutJudge
    {
        // ✅ ancienne signature
        public static bool TryIsIn(ClickLineDetector detector, object lineLock, PointF p, out bool isIn)
        {
            return TryIsIn(detector, lineLock, p, out isIn, out _, leftIsIn: true, epsilonPx: 0f);
        }

        // ✅ nouvelle signature robuste
        public static bool TryIsIn(
            ClickLineDetector detector,
            object lineLock,
            PointF p,
            out bool isIn,
            out float signedDistancePx,
            bool leftIsIn = true,
            float epsilonPx = 3f)
        {
            isIn = false;
            signedDistancePx = 0f;

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

            if (dy > 0f) { dx = -dx; dy = -dy; }

            float vx = p.X - x0;
            float vy = p.Y - y0;

            float cross = vx * dy - vy * dx;
            signedDistancePx = cross;

            if (Math.Abs(cross) <= Math.Max(0f, epsilonPx))
            {
                return true; // sur la ligne / proche
            }

            bool leftSide = cross > 0f;
            isIn = leftIsIn ? leftSide : !leftSide;
            return true;
        }
    }
}