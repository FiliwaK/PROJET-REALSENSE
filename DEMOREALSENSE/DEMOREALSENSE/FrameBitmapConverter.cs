using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Conversion image (RGB8) vers Bitmap + dessin overlay.
    /// </summary>
    public static class FrameBitmapConverter
    {
        public static Bitmap RgbToBitmap24bpp(byte[] rgb, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);

            try
            {
                int dstStride = data.Stride;
                int srcStride = w * 3;

                for (int y = 0; y < h; y++)
                {
                    IntPtr dstRow = data.Scan0 + y * dstStride;
                    int si = y * srcStride;

                    byte[] row = new byte[srcStride];
                    Buffer.BlockCopy(rgb, si, row, 0, srcStride);

                    // RGB -> BGR
                    for (int i = 0; i < row.Length; i += 3)
                    {
                        byte r = row[i + 0];
                        byte g = row[i + 1];
                        byte b = row[i + 2];
                        row[i + 0] = b;
                        row[i + 1] = g;
                        row[i + 2] = r;
                    }

                    Marshal.Copy(row, 0, dstRow, row.Length);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            return bmp;
        }

        public static void DrawGreenBox(Bitmap bmp, int px, int py, int boxHalf = 12)
        {
            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.Lime, 2);

            int x0 = px - boxHalf;
            int y0 = py - boxHalf;
            int size = boxHalf * 2;

            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x0 + size >= bmp.Width) x0 = Math.Max(0, bmp.Width - size - 1);
            if (y0 + size >= bmp.Height) y0 = Math.Max(0, bmp.Height - size - 1);

            g.DrawRectangle(pen, x0, y0, size, size);
            g.FillEllipse(Brushes.Lime, px - 2, py - 2, 5, 5);
        }
    }
}