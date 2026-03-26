using System.Drawing;
using System.Drawing.Drawing2D;

namespace DEMOREALSENSE
{
    public sealed class OverlayRenderer
    {
        public int ManualBoxHalf { get; set; } = 12;

        public void DrawManualBox(Bitmap bmp, int x, int y)
            => FrameBitmapConverter.DrawGreenBox(bmp, x, y, ManualBoxHalf);

        public void DrawAutoCircle(Bitmap bmp, int x, int y, int radiusPx = 12)
        {
            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.DeepSkyBlue, 2f);
            g.DrawEllipse(pen, x - radiusPx, y - radiusPx, radiusPx * 2, radiusPx * 2);
        }

        public void DrawIaCircle(Bitmap bmp, int x, int y, int radiusPx = 16)
        {
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.Magenta, 2.5f);
            g.DrawRectangle(pen, x - radiusPx, y - radiusPx, radiusPx * 2, radiusPx * 2);
            using var penC = new Pen(Color.Magenta, 1.5f);
            g.DrawLine(penC, x - 4, y, x + 4, y);
            g.DrawLine(penC, x, y - 4, x, y + 4);
        }

        public void DrawGroundDebug(Bitmap bmp, float x, float yGround)
        {
            using var g = Graphics.FromImage(bmp);
            g.FillEllipse(Brushes.YellowGreen, x - 2, yGround - 2, 4, 4);
        }

        /// <summary>
        /// Dessine la ligne avec sa largeur réelle en pixels.
        /// Bande jaune semi-transparente + bordure blanche (IN) + rouge (OUT) + axe jaune.
        /// Points Ctrl+Click affichés en vert.
        /// </summary>
        public void DrawLineOverlay(Bitmap bmp, ClickLineDetector lineDetector, object lineLock,
                                    float lineWidthPx = 6f)
        {
            bool hasLine;
            lock (lineLock)
            {
                hasLine = lineDetector.HasLine;
                if (!hasLine && lineDetector.Samples.Count == 0) return;
            }

            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Points cliqués
            lock (lineLock)
            {
                foreach (var p in lineDetector.Samples)
                    g.FillEllipse(Brushes.Lime, p.X - 3, p.Y - 3, 6, 6);
            }

            if (!hasLine) return;

            var bounds = new RectangleF(0, 0, bmp.Width - 1, bmp.Height - 1);
            if (!lineDetector.TryGetSegmentWithin(bounds, out var a, out var b)) return;

            float w = System.Math.Max(2f, lineWidthPx);

            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = System.MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1f) return;

            float nx = -dy / len * (w / 2f);
            float ny = dx / len * (w / 2f);

            var pts = new PointF[]
            {
                new PointF(a.X + nx, a.Y + ny),
                new PointF(b.X + nx, b.Y + ny),
                new PointF(b.X - nx, b.Y - ny),
                new PointF(a.X - nx, a.Y - ny),
            };

            using var fillBrush = new SolidBrush(Color.FromArgb(80, 255, 220, 0));
            g.FillPolygon(fillBrush, pts);

            using var penIn = new Pen(Color.White, 1.5f);
            using var penOut = new Pen(Color.Red, 1.5f);
            using var penC = new Pen(Color.Yellow, 1.5f) { DashStyle = DashStyle.Dash };

            g.DrawLine(penIn, new PointF(a.X + nx, a.Y + ny), new PointF(b.X + nx, b.Y + ny));
            g.DrawLine(penOut, new PointF(a.X - nx, a.Y - ny), new PointF(b.X - nx, b.Y - ny));
            g.DrawLine(penC, a, b);
        }

        public void DrawCourtOverlay(Bitmap bmp, CourtArea court)
        {
            if (court == null) return;
            var pts = court.Points;
            if (pts.Count == 0) return;
            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.Cyan, 2f);
            foreach (var p in pts)
                g.FillEllipse(Brushes.Cyan, p.X - 4, p.Y - 4, 8, 8);
            if (pts.Count >= 2)
            {
                for (int i = 0; i < pts.Count - 1; i++)
                    g.DrawLine(pen, pts[i], pts[i + 1]);
                if (court.HasCourt)
                    g.DrawLine(pen, pts[pts.Count - 1], pts[0]);
            }
        }
    }
}