using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class ImpactDetector
    {
        public float MinPrevSpeed = 120f;     // avant impact
        public float MaxNowSpeed = 35f;       // après impact (ralentit fortement)
        public float MaxMovePx = 4f;          // quasi immobile
        public int NeedStableFrames = 2;      // pour valider

        private int _stableCount = 0;

        public void Reset() => _stableCount = 0;

        public bool TryDetectFirstImpact(PointF vPrev, PointF vNow, float movePx)
        {
            float spPrev = Length(vPrev);
            float spNow = Length(vNow);

            if (spPrev < MinPrevSpeed)
            {
                _stableCount = 0;
                return false;
            }

            if (spNow <= MaxNowSpeed && movePx <= MaxMovePx)
            {
                _stableCount++;
                if (_stableCount >= NeedStableFrames)
                {
                    _stableCount = 0;
                    return true;
                }
                return false;
            }

            _stableCount = 0;
            return false;
        }

        private static float Length(PointF v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
    }
}