using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class OverlayRenderer
    {
        public int ManualBoxHalf { get; set; } = 12;

        // === compat noms ===
        public void DrawManualTracker(Bitmap bmp, int x, int y) => DrawManualBox(bmp, x, y);
        public void DrawAutoBall(Bitmap bmp, int x, int y, int radiusPx = 12) => DrawAutoCircle(bmp, x, y, radiusPx);
        public void DrawLine(Bitmap bmp, ClickLineDetector lineDetector, object lineLock) => DrawLineOverlay(bmp, lineDetector, lineLock);

        // === rendu ===
        public void DrawManualBox(Bitmap bmp, int x, int y)
        {
            FrameBitmapConverter.DrawGreenBox(bmp, x, y, ManualBoxHalf);
        }

        public void DrawAutoCircle(Bitmap bmp, int x, int y, int radiusPx = 12)
        {
            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.DeepSkyBlue, 2f);
            g.DrawEllipse(pen, x - radiusPx, y - radiusPx, radiusPx * 2, radiusPx * 2);
        }

        public void DrawImpactCross(Bitmap bmp, float x, float y)
        {
            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.Yellow, 3f);

            g.DrawLine(pen, x - 10, y - 10, x + 10, y + 10);
            g.DrawLine(pen, x - 10, y + 10, x + 10, y - 10);
        }

        public void DrawGroundDebug(Bitmap bmp, float x, float yGround)
        {
            using var g = Graphics.FromImage(bmp);
            g.FillEllipse(Brushes.YellowGreen, x - 2, yGround - 2, 4, 4);
        }

        public void DrawLineOverlay(Bitmap bmp, ClickLineDetector lineDetector, object lineLock)
        {
            bool hasLine;
            lock (lineLock)
            {
                hasLine = lineDetector.HasLine;
                if (!hasLine && lineDetector.Samples.Count == 0) return;
            }

            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

            // points cliqués
            lock (lineLock)
            {
                for (int i = 0; i < lineDetector.Samples.Count; i++)
                {
                    var p = lineDetector.Samples[i];
                    g.FillEllipse(Brushes.Lime, p.X - 2, p.Y - 2, 4, 4);
                }
            }

            if (!hasLine) return;

            var bounds = new RectangleF(0, 0, bmp.Width - 1, bmp.Height - 1);
            if (lineDetector.TryGetSegmentWithin(bounds, out var a, out var b))
            {
                using var pen = new Pen(Color.Red, 2f);
                g.DrawLine(pen, a, b);
            }
        }
    }
}