using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Détecte automatiquement la balle (BallDetector) puis
    /// démarre TemplateTracker pour un suivi stable.
    /// En cas de perte, ré-acquiert automatiquement.
    /// </summary>
    public sealed class AutoTemplateFollower
    {
        private readonly BallDetector _detector;
        private readonly TemplateTracker _template;

        public AutoTemplateFollower(BallDetector detector, TemplateTracker templateTracker)
        {
            _detector = detector ?? throw new ArgumentNullException(nameof(detector));
            _template = templateTracker ?? throw new ArgumentNullException(nameof(templateTracker));
        }

        public int ReacquireEveryNFrames { get; set; } = 2;
        public int RoiHalfSize { get; set; } = 220;
        public int MinConfirmFrames { get; set; } = 2;

        private int _frameCount = 0;
        private int _confirm = 0;
        private Point _last = new Point(-1, -1);

        public void Reset()
        {
            _confirm = 0;
            _last = new Point(-1, -1);
        }

        /// <summary>
        /// Retourne true si on a une position utilisable (détectée ou trackée).
        /// bx/by = position estimée.
        /// </summary>
        public bool TryUpdate(byte[] rgb, int w, int h, Bitmap bmp24, out int bx, out int by)
        {
            bx = by = -1;
            if (rgb == null || bmp24 == null) return false;

            _frameCount++;

            // 1) Si TemplateTracker actif, c'est le signal le plus stable
            if (_template.IsTracking)
            {
                if (_template.TryUpdate(rgb, w, h))
                {
                    _last = new Point(_template.X, _template.Y);
                    bx = _template.X;
                    by = _template.Y;
                    _confirm = MinConfirmFrames;
                    return true;
                }

                // perdu -> stop + tentative reacquire
                _template.Stop();
                _confirm = 0;
            }

            // 2) Réacquisition via BallDetector (pas forcément chaque frame)
            if ((_frameCount % ReacquireEveryNFrames) != 0)
                return false;

            if (!TryDetectInRoi(bmp24, _last, out int cx, out int cy))
                return false;

            _confirm++;
            _last = new Point(cx, cy);

            // Après X confirmations => start TemplateTracker auto
            if (_confirm >= MinConfirmFrames)
            {
                bool started = _template.TryStart(rgb, w, h, cx, cy);
                if (started)
                {
                    bx = cx; by = cy;
                    return true;
                }
                _confirm = 0;
            }

            // Détecté mais pas encore lock template
            bx = cx; by = cy;
            return true;
        }

        private bool TryDetectInRoi(Bitmap bmp24, Point last, out int cx, out int cy)
        {
            cx = cy = -1;

            // si pas de position => global
            if (last.X < 0 || last.Y < 0)
            {
                if (_detector.TryDetect(bmp24, out int x, out int y, out _))
                {
                    cx = x; cy = y;
                    return true;
                }
                return false;
            }

            Rectangle roi = BuildRoi(bmp24.Width, bmp24.Height, last, RoiHalfSize);
            using var crop = bmp24.Clone(roi, bmp24.PixelFormat);

            if (_detector.TryDetect(crop, out int rx, out int ry, out _))
            {
                cx = roi.X + rx;
                cy = roi.Y + ry;
                return true;
            }

            return false;
        }

        private static Rectangle BuildRoi(int w, int h, Point center, int half)
        {
            int x = center.X - half;
            int y = center.Y - half;
            int rw = half * 2;
            int rh = half * 2;

            if (x < 0) { rw += x; x = 0; }
            if (y < 0) { rh += y; y = 0; }
            if (x + rw > w) rw = w - x;
            if (y + rh > h) rh = h - y;

            rw = Math.Max(1, rw);
            rh = Math.Max(1, rh);

            return new Rectangle(x, y, rw, rh);
        }
    }
}