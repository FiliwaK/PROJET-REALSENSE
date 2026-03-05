using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DEMOREALSENSE
{
    public sealed class BallDetector
    {
        // Couleur cible (exemple) + tolérances
        public byte TargetR = 220;
        public byte TargetG = 120;
        public byte TargetB = 40;

        public int TolR = 60;
        public int TolG = 60;
        public int TolB = 60;

        public int MinBlobPixels = 120; // à ajuster selon ta scène

        public bool TryDetect(Bitmap bmp, out int cx, out int cy, out int approxRadius)
        {
            cx = cy = approxRadius = 0;

            // On force 24bpp au cas où
            if (bmp.PixelFormat != PixelFormat.Format24bppRgb)
            {
                using var tmp = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(tmp))
                    g.DrawImageUnscaled(bmp, 0, 0);

                return TryDetect(tmp, out cx, out cy, out approxRadius);
            }

            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                int w = bmp.Width;
                int h = bmp.Height;
                int stride = data.Stride;
                int bytes = stride * h;

                byte[] buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);

                long sumX = 0, sumY = 0;
                int count = 0;

                int minX = w, minY = h, maxX = 0, maxY = 0;

                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = row + x * 3;

                        byte b = buffer[i + 0];
                        byte g = buffer[i + 1];
                        byte r = buffer[i + 2];

                        if (Math.Abs(r - TargetR) <= TolR &&
                            Math.Abs(g - TargetG) <= TolG &&
                            Math.Abs(b - TargetB) <= TolB)
                        {
                            count++;
                            sumX += x;
                            sumY += y;

                            if (x < minX) minX = x;
                            if (y < minY) minY = y;
                            if (x > maxX) maxX = x;
                            if (y > maxY) maxY = y;
                        }
                    }
                }

                if (count < MinBlobPixels) return false;

                cx = (int)(sumX / count);
                cy = (int)(sumY / count);

                int bw = (maxX - minX + 1);
                int bh = (maxY - minY + 1);
                approxRadius = Math.Max(bw, bh) / 2;

                // filtres simples (évite détections aléatoires)
                float fillRatio = count / (float)(bw * bh);
                if (fillRatio < 0.25f) return false;

                float aspect = bw / (float)Math.Max(1, bh);
                if (aspect < 0.4f || aspect > 2.5f) return false;

                return true;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
    }
}