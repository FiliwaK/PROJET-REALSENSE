using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class ImpactDetector
    {
        // Vy flip (descend -> remonte) : en image, Y vers le bas
        public float MinVyDown { get; set; } = 120f;
        public float MinVyUp { get; set; } = 120f;

        // changement d'angle (optionnel) : cos(angle) petit => gros virage
        public float MaxCosAngle { get; set; } = 0.25f; // ~75° ou plus (assez violent)

        // anti-rebond multiple
        public int CooldownMs { get; set; } = 600;

        private long _lastImpactTicks = 0;

        public void Reset() => _lastImpactTicks = 0;

        public bool TryDetectImpact(long nowTicks, PointF vPrev, PointF vNow)
        {
            long cd = System.TimeSpan.FromMilliseconds(CooldownMs).Ticks;
            if (_lastImpactTicks != 0 && nowTicks - _lastImpactTicks < cd)
                return false;

            // 1) Rebond vertical : descend (vy +) puis remonte (vy -)
            bool vyFlip = (vPrev.Y > +MinVyDown && vNow.Y < -MinVyUp);

            // 2) Gros virage : angle entre vPrev et vNow
            bool angleBreak = false;
            float a = Len(vPrev);
            float b = Len(vNow);
            if (a > 1e-3f && b > 1e-3f)
            {
                float cos = (vPrev.X * vNow.X + vPrev.Y * vNow.Y) / (a * b);
                // cos proche de 1 = même direction, cos proche de -1 = opposé
                angleBreak = cos < MaxCosAngle;
            }

            if (vyFlip || angleBreak)
            {
                _lastImpactTicks = nowTicks;
                return true;
            }

            return false;
        }

        private static float Len(PointF v) => (float)System.Math.Sqrt(v.X * v.X + v.Y * v.Y);
    }
}