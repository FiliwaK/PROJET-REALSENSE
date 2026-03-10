using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class GroundEstimator
    {
        // tolérances (en pixels)
        public float NearGroundPx { get; set; } = 35f;
        public float AboveGroundPx { get; set; } = 80f;

        // IMPORTANT : le sol est généralement *un peu* sous la ligne
        public float GroundOffsetPx { get; set; } = 12f;

        public bool TryGetGroundY(ClickLineDetector detector, object lineLock, float x, out float yGround)
        {
            yGround = 0;

            ClickLineDetector.LineModel line;
            lock (lineLock)
            {
                if (!detector.HasLine) return false;
                line = detector.Line;
            }

            // modèle : point P0 + t*dir
            // On veut y = y0 + t*dy quand x = x0 + t*dx
            float dx = line.Direction.X;
            float dy = line.Direction.Y;

            if (System.Math.Abs(dx) < 1e-5f)
            {
                // ligne quasi verticale -> pas bon pour sol(x)
                return false;
            }

            float t = (x - line.Point.X) / dx;
            float yOnLine = line.Point.Y + t * dy;

            yGround = yOnLine + GroundOffsetPx;
            return true;
        }

        public bool IsClearlyInAir(ClickLineDetector detector, object lineLock, PointF p, out float yGround)
        {
            yGround = 0;
            if (!TryGetGroundY(detector, lineLock, p.X, out yGround)) return false;
            return (yGround - p.Y) >= AboveGroundPx;
        }

        public bool IsNearGround(ClickLineDetector detector, object lineLock, PointF p, out float yGround)
        {
            yGround = 0;
            if (!TryGetGroundY(detector, lineLock, p.X, out yGround)) return false;
            return System.Math.Abs(p.Y - yGround) <= NearGroundPx;
        }
    }
}