using System;
using System.Collections.Generic;
using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Terrain défini par 4 points (Alt+Click x4).
    /// Points triés autour du centre (clockwise) pour éviter les polygones croisés.
    /// Bord = IN (edgeEpsPx).
    /// </summary>
    public sealed class CourtArea
    {
        private readonly List<PointF> _pts = new List<PointF>(4);

        public IReadOnlyList<PointF> Points => _pts;
        public bool HasCourt => _pts.Count == 4;

        public void Clear() => _pts.Clear();

        public void AddPoint(PointF p)
        {
            if (_pts.Count >= 4) return;
            _pts.Add(p);

            if (_pts.Count == 4)
                SortClockwise();
        }

        private void SortClockwise()
        {
            float cx = 0, cy = 0;
            for (int i = 0; i < _pts.Count; i++)
            {
                cx += _pts[i].X;
                cy += _pts[i].Y;
            }
            cx /= _pts.Count;
            cy /= _pts.Count;

            _pts.Sort((a, b) =>
            {
                double aa = Math.Atan2(a.Y - cy, a.X - cx);
                double bb = Math.Atan2(b.Y - cy, b.X - cx);
                return aa.CompareTo(bb);
            });
        }

        /// <summary>
        /// Test point-in-polygon (ray casting). Sur bord = IN.
        /// </summary>
        public bool Contains(PointF p, float edgeEpsPx = 3f)
        {
            if (!HasCourt) return false;

            // Sur une arête => IN
            for (int i = 0; i < 4; i++)
            {
                var a = _pts[i];
                var b = _pts[(i + 1) % 4];
                if (DistancePointToSegment(p, a, b) <= edgeEpsPx)
                    return true;
            }

            // Ray casting
            bool inside = false;
            for (int i = 0, j = 3; i < 4; j = i++)
            {
                var pi = _pts[i];
                var pj = _pts[j];

                bool intersect =
                    ((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                    (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y + 1e-6f) + pi.X);

                if (intersect) inside = !inside;
            }
            return inside;
        }

        private static float DistancePointToSegment(PointF p, PointF a, PointF b)
        {
            float vx = b.X - a.X;
            float vy = b.Y - a.Y;
            float wx = p.X - a.X;
            float wy = p.Y - a.Y;

            float c1 = vx * wx + vy * wy;
            if (c1 <= 0) return Dist(p, a);

            float c2 = vx * vx + vy * vy;
            if (c2 <= 1e-6f) return Dist(p, a);

            float t = c1 / c2;
            if (t >= 1) return Dist(p, b);

            var proj = new PointF(a.X + t * vx, a.Y + t * vy);
            return Dist(p, proj);
        }

        private static float Dist(PointF p1, PointF p2)
        {
            float dx = p1.X - p2.X;
            float dy = p1.Y - p2.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}