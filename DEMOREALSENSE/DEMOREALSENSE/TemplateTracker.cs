using System;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Tracking simple par Template Matching (SAD) en grayscale.
    /// </summary>
    public sealed class TemplateTracker
    {
        public bool IsTracking { get; private set; }
        public int X { get; private set; } = -1;
        public int Y { get; private set; } = -1;

        private byte[]? _tplGray;

        public int TemplateSize { get; set; } = 31;  // impair
        public int SearchRadius { get; set; } = 40;
        public int MaxAcceptFactor { get; set; } = 25;

        public bool TryStart(byte[] rgb, int w, int h, int cx, int cy)
        {
            if (!TryCreateTemplate(rgb, w, h, cx, cy, TemplateSize, out var tpl))
                return false;

            _tplGray = tpl;
            X = cx;
            Y = cy;
            IsTracking = true;
            return true;
        }

        public void Stop()
        {
            IsTracking = false;
            X = Y = -1;
            _tplGray = null;
        }

        public bool TryUpdate(byte[] rgb, int w, int h)
        {
            if (!IsTracking || _tplGray == null) return false;

            if (TryTemplateTrack(rgb, w, h, _tplGray, TemplateSize, X, Y, SearchRadius,
                out int nx, out int ny, out int bestScore))
            {
                X = nx; Y = ny;
                return true;
            }

            return false;
        }

        private static bool TryCreateTemplate(byte[] rgb, int w, int h, int cx, int cy, int size, out byte[] tplGray)
        {
            tplGray = Array.Empty<byte>();
            if (size < 9) size = 9;
            if ((size & 1) == 0) size++;

            int half = size / 2;
            int x0 = cx - half;
            int y0 = cy - half;
            int x1 = cx + half;
            int y1 = cy + half;

            if (x0 < 0 || y0 < 0 || x1 >= w || y1 >= h) return false;

            tplGray = new byte[size * size];
            int ti = 0;

            for (int y = y0; y <= y1; y++)
            {
                int row = y * w * 3;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x * 3;
                    byte r = rgb[i + 0];
                    byte g = rgb[i + 1];
                    byte b = rgb[i + 2];
                    int gray = (r * 30 + g * 59 + b * 11) / 100;
                    tplGray[ti++] = (byte)gray;
                }
            }

            return true;
        }

        private bool TryTemplateTrack(
            byte[] rgb, int w, int h,
            byte[] tplGray, int tplSize,
            int cx, int cy, int searchRadius,
            out int bestX, out int bestY, out int bestScore)
        {
            bestX = cx;
            bestY = cy;
            bestScore = int.MaxValue;

            int half = tplSize / 2;
            if (cx < 0 || cy < 0 || cx >= w || cy >= h) return false;

            int sx0 = Math.Max(half, cx - searchRadius);
            int sy0 = Math.Max(half, cy - searchRadius);
            int sx1 = Math.Min(w - 1 - half, cx + searchRadius);
            int sy1 = Math.Min(h - 1 - half, cy + searchRadius);
            if (sx0 > sx1 || sy0 > sy1) return false;

            int maxAccept = tplSize * tplSize * MaxAcceptFactor;

            for (int y = sy0; y <= sy1; y++)
            {
                for (int x = sx0; x <= sx1; x++)
                {
                    int score = SadAt(rgb, w, x - half, y - half, tplGray, tplSize);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            return bestScore <= maxAccept;
        }

        private static int SadAt(byte[] rgb, int w, int x0, int y0, byte[] tplGray, int size)
        {
            int score = 0;
            int ti = 0;

            for (int y = 0; y < size; y++)
            {
                int row = (y0 + y) * w * 3;
                for (int x = 0; x < size; x++)
                {
                    int i = row + (x0 + x) * 3;
                    byte r = rgb[i + 0];
                    byte g = rgb[i + 1];
                    byte b = rgb[i + 2];

                    int gray = (r * 30 + g * 59 + b * 11) / 100;
                    int diff = gray - tplGray[ti++];
                    if (diff < 0) diff = -diff;
                    score += diff;
                }
            }

            return score;
        }
    }
}