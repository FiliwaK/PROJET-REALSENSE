using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class AutoBallTracker
    {
        private readonly BallDetector _detector;

        public AutoBallTracker(BallDetector detector)
        {
            _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        }

        public int RoiHalfSize { get; set; } = 140;
        public float SmoothAlpha { get; set; } = 0.35f;
        public int MaxMisses { get; set; } = 6;

        public bool HasTarget { get; private set; }
        public PointF SmoothedPos { get; private set; }
        public int LastRadius { get; private set; }

        private int _misses;

        public void Reset()
        {
            HasTarget = false;
            SmoothedPos = default;
            LastRadius = 0;
            _misses = 0;
        }

        /// <summary>
        /// Update le suivi.
        /// Retourne true si la détection est confirmée sur CETTE frame.
        /// HasTarget peut rester true si on perd quelques frames (tolérance).
        /// </summary>
        public bool Update(Bitmap frame, out int x, out int y, out int r)
        {
            x = y = r = 0;
            if (frame == null) return false;

            if (HasTarget)
            {
                Rectangle roi = BuildRoi(frame.Width, frame.Height, SmoothedPos, RoiHalfSize);

                using (Bitmap crop = frame.Clone(roi, frame.PixelFormat))
                {
                    if (_detector.TryDetect(crop, out int cx, out int cy, out int rr))
                    {
                        int gx = roi.X + cx;
                        int gy = roi.Y + cy;

                        ApplySmoothing(gx, gy);
                        LastRadius = rr;

                        x = (int)SmoothedPos.X;
                        y = (int)SmoothedPos.Y;
                        r = Math.Max(6, LastRadius);

                        _misses = 0;
                        return true;
                    }
                }

                _misses++;
                if (_misses <= MaxMisses)
                {
                    // perte temporaire : on renvoie la dernière position lissée
                    x = (int)SmoothedPos.X;
                    y = (int)SmoothedPos.Y;
                    r = Math.Max(6, LastRadius);
                    return false;
                }

                // perdu
                Reset();
            }

            // Recherche globale
            if (_detector.TryDetect(frame, out int fx, out int fy, out int fr))
            {
                HasTarget = true;
                SmoothedPos = new PointF(fx, fy);
                LastRadius = fr;
                _misses = 0;

                x = fx;
                y = fy;
                r = Math.Max(6, fr);
                return true;
            }

            return false;
        }

        private void ApplySmoothing(int x, int y)
        {
            PointF p = new PointF(x, y);
            SmoothedPos = new PointF(
                SmoothedPos.X + (p.X - SmoothedPos.X) * SmoothAlpha,
                SmoothedPos.Y + (p.Y - SmoothedPos.Y) * SmoothAlpha
            );
        }

        private static Rectangle BuildRoi(int w, int h, PointF center, int half)
        {
            int x = (int)center.X - half;
            int y = (int)center.Y - half;
            int rw = half * 2;
            int rh = half * 2;

            if (x < 0) { rw += x; x = 0; }
            if (y < 0) { rh += y; y = 0; }
            if (x + rw > w) rw = w - x;
            if (y + rh > h) rh = h - y;

            if (rw < 1) rw = 1;
            if (rh < 1) rh = 1;

            return new Rectangle(x, y, rw, rh);
        }
    }
}