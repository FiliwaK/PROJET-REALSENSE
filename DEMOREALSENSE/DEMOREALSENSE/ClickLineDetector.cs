using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DEMOREALSENSE
{
    public sealed class ClickLineDetector
    {
        private readonly List<PointF> _samples = new();

        public int MinPointsToFit { get; set; } = 6;
        public int RansacIterations { get; set; } = 200;
        public float InlierThresholdPx { get; set; } = 6f;
        public int MinInliers { get; set; } = 6;

        public bool HasLine { get; private set; }
        public LineModel Line { get; private set; }

        public IReadOnlyList<PointF> Samples => _samples;

        public void Clear()
        {
            _samples.Clear();
            HasLine = false;
            Line = default;
        }

        public bool AddClick(PointF p)
        {
            _samples.Add(p);
            return TryFit();
        }

        public bool TryFit()
        {
            if (_samples.Count < MinPointsToFit)
            {
                HasLine = false;
                return false;
            }

            if (!TryFitRansac(_samples, out var model, out var inliers))
            {
                HasLine = false;
                return false;
            }

            if (!TryFitPca(inliers, out var refined))
            {
                HasLine = false;
                return false;
            }

            Line = refined;
            HasLine = true;
            return true;
        }

        public bool TryGetSegmentWithin(RectangleF bounds, out PointF a, out PointF b)
        {
            a = default; b = default;
            if (!HasLine) return false;

            var pts = new List<PointF>(4);

            if (Math.Abs(Line.Direction.X) > 1e-6f)
            {
                float tL = (bounds.Left - Line.Point.X) / Line.Direction.X;
                float yL = Line.Point.Y + tL * Line.Direction.Y;
                if (yL >= bounds.Top && yL <= bounds.Bottom) pts.Add(new PointF(bounds.Left, yL));

                float tR = (bounds.Right - Line.Point.X) / Line.Direction.X;
                float yR = Line.Point.Y + tR * Line.Direction.Y;
                if (yR >= bounds.Top && yR <= bounds.Bottom) pts.Add(new PointF(bounds.Right, yR));
            }

            if (Math.Abs(Line.Direction.Y) > 1e-6f)
            {
                float tT = (bounds.Top - Line.Point.Y) / Line.Direction.Y;
                float xT = Line.Point.X + tT * Line.Direction.X;
                if (xT >= bounds.Left && xT <= bounds.Right) pts.Add(new PointF(xT, bounds.Top));

                float tB = (bounds.Bottom - Line.Point.Y) / Line.Direction.Y;
                float xB = Line.Point.X + tB * Line.Direction.X;
                if (xB >= bounds.Left && xB <= bounds.Right) pts.Add(new PointF(xB, bounds.Bottom));
            }

            pts = pts.Distinct(new PointFComparer(0.5f)).ToList();
            if (pts.Count < 2) return false;

            float bestD = -1f;
            PointF bestA = default, bestB = default;

            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    float dx = pts[i].X - pts[j].X;
                    float dy = pts[i].Y - pts[j].Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > bestD)
                    {
                        bestD = d2;
                        bestA = pts[i];
                        bestB = pts[j];
                    }
                }

            a = bestA;
            b = bestB;
            return true;
        }

        public readonly struct LineModel
        {
            public readonly PointF Point;
            public readonly PointF Direction; // unit

            public LineModel(PointF point, PointF dirUnit)
            {
                Point = point;
                Direction = dirUnit;
            }
        }

        private bool TryFitRansac(List<PointF> points, out LineModel bestModel, out List<PointF> bestInliers)
        {
            bestModel = default;
            bestInliers = new List<PointF>();
            if (points.Count < 2) return false;

            var rnd = new Random(12345);

            for (int it = 0; it < RansacIterations; it++)
            {
                var p1 = points[rnd.Next(points.Count)];
                var p2 = points[rnd.Next(points.Count)];
                if (Distance(p1, p2) < 2f) continue;

                var model = BuildFromTwoPoints(p1, p2);

                var inliers = new List<PointF>(points.Count);
                foreach (var p in points)
                {
                    if (PointLineDistancePx(p, model) <= InlierThresholdPx)
                        inliers.Add(p);
                }

                if (inliers.Count > bestInliers.Count)
                {
                    bestInliers = inliers;
                    bestModel = model;
                }
            }

            if (bestInliers.Count < Math.Max(MinInliers, MinPointsToFit))
                return false;

            return true;
        }

        private static LineModel BuildFromTwoPoints(PointF a, PointF b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = (float)Math.Sqrt(dx * dx + dy * dy);
            var dir = new PointF(dx / len, dy / len);
            return new LineModel(new PointF((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f), dir);
        }

        private static bool TryFitPca(List<PointF> pts, out LineModel model)
        {
            model = default;
            if (pts == null || pts.Count < 2) return false;

            float mx = 0, my = 0;
            foreach (var p in pts) { mx += p.X; my += p.Y; }
            mx /= pts.Count; my /= pts.Count;

            float sxx = 0, sxy = 0, syy = 0;
            foreach (var p in pts)
            {
                float x = p.X - mx;
                float y = p.Y - my;
                sxx += x * x;
                sxy += x * y;
                syy += y * y;
            }

            float theta = 0.5f * (float)Math.Atan2(2f * sxy, (sxx - syy));
            float dx = (float)Math.Cos(theta);
            float dy = (float)Math.Sin(theta);

            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6f) return false;

            var dir = new PointF(dx / len, dy / len);
            model = new LineModel(new PointF(mx, my), dir);
            return true;
        }

        public static float PointLineDistancePx(PointF p, LineModel line)
        {
            float vx = p.X - line.Point.X;
            float vy = p.Y - line.Point.Y;
            float cross = Math.Abs(vx * line.Direction.Y - vy * line.Direction.X);
            return cross;
        }

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private sealed class PointFComparer : IEqualityComparer<PointF>
        {
            private readonly float _eps;
            public PointFComparer(float eps) => _eps = eps;

            public bool Equals(PointF a, PointF b)
                => Math.Abs(a.X - b.X) <= _eps && Math.Abs(a.Y - b.Y) <= _eps;

            public int GetHashCode(PointF p)
                => ((int)(p.X / _eps) * 397) ^ (int)(p.Y / _eps);
        }
    }
}