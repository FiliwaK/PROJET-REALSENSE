using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class GroundEstimator
    {
        public float NearGroundPx { get; set; } = 35f;
        public float AboveGroundPx { get; set; } = 80f;
        public float ContactDepthEpsMeters { get; set; } = 0.035f;

        public bool TryGetGroundY(ClickLineDetector detector, object lineLock,
                                   int x, out float yGround)
        {
            yGround = 0f;

            ClickLineDetector.LineModel line;
            lock (lineLock)
            {
                if (!detector.HasLine) return false;
                line = detector.Line;
            }

            float x0 = line.Point.X, y0 = line.Point.Y;
            float dx = line.Direction.X, dy = line.Direction.Y;

            if (Math.Abs(dx) < 1e-6f) { yGround = y0; return true; }

            float t = (x - x0) / dx;
            yGround = y0 + t * dy;
            return true;
        }

        public bool IsClearlyInAir(float y, float yGround)
            => y < (yGround - AboveGroundPx);

        public bool IsContactWithGround(int bx, int by, float yGround,
                                         ushort ballRaw, ushort groundRaw, float depthUnits)
        {
            if (Math.Abs(by - yGround) > NearGroundPx) return false;
            if (ballRaw == 0 || groundRaw == 0) return false;
            if (Math.Abs(ballRaw * depthUnits - groundRaw * depthUnits) > ContactDepthEpsMeters)
                return false;
            return true;
        }
    }
}