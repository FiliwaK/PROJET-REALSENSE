using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class OverlayRenderer
    {
        public int ManualBoxHalf { get; set; } = 12;

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

            // Points cliqués
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

        /// <summary>
        /// ✅ Affiche le terrain défini par 4 points (Alt+Click x4).
        /// - Points: petits ronds cyan
        /// - Lignes: polygone cyan
        /// </summary>
        public void DrawCourtOverlay(Bitmap bmp, CourtArea court)
        {
            if (court == null) return;
            var pts = court.Points;
            if (pts.Count == 0) return;

            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.Cyan, 2f);

            // points
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                g.FillEllipse(Brushes.Cyan, p.X - 4, p.Y - 4, 8, 8);
            }

            // lignes si 2+ points
            if (pts.Count >= 2)
            {
                for (int i = 0; i < pts.Count - 1; i++)
                    g.DrawLine(pen, pts[i], pts[i + 1]);

                // si terrain complet: fermer le polygone
                if (court.HasCourt)
                    g.DrawLine(pen, pts[pts.Count - 1], pts[0]);
            }
        }
    }
}