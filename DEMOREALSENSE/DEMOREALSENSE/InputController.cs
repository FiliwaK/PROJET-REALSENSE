using System;
using System.Drawing;
using System.Windows.Forms;

namespace DEMOREALSENSE
{
    public sealed class InputController
    {
        private readonly PictureBox _pictureBox;
        private readonly SnapshotBuffer _snapshots;

        public InputController(PictureBox pictureBox, SnapshotBuffer snapshots)
        {
            _pictureBox = pictureBox;
            _snapshots = snapshots;
        }

        public bool TryGetClickPixel(Point mouseLocation, out int x, out int y)
        {
            x = y = -1;

            using var img = _snapshots.TryClone();
            if (img == null) return false;

            (x, y) = TranslateZoomMousePositionToImagePixel(_pictureBox, img, mouseLocation);
            return x >= 0 && y >= 0;
        }

        private static (int x, int y) TranslateZoomMousePositionToImagePixel(PictureBox pb, Image img, Point mouse)
        {
            float imageAspect = (float)img.Width / img.Height;
            float boxAspect = (float)pb.Width / pb.Height;

            int drawWidth, drawHeight;
            int offsetX = 0, offsetY = 0;

            if (imageAspect > boxAspect)
            {
                drawWidth = pb.Width;
                drawHeight = (int)(pb.Width / imageAspect);
                offsetY = (pb.Height - drawHeight) / 2;
            }
            else
            {
                drawHeight = pb.Height;
                drawWidth = (int)(pb.Height * imageAspect);
                offsetX = (pb.Width - drawWidth) / 2;
            }

            int rx = mouse.X - offsetX;
            int ry = mouse.Y - offsetY;

            if (rx < 0 || ry < 0 || rx >= drawWidth || ry >= drawHeight) return (-1, -1);

            int imgX = (int)(rx * (img.Width / (float)drawWidth));
            int imgY = (int)(ry * (img.Height / (float)drawHeight));
            return (imgX, imgY);
        }
    }
}