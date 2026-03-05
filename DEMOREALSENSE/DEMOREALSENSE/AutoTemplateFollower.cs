using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Détecte automatiquement la balle (BallDetector) puis
    /// démarre le TemplateTracker pour un suivi stable.
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

        public bool IsActive { get; private set; } = true;

        // Quand on perd, combien de frames on tente de ré-acquérir
        public int ReacquireEveryNFrames { get; set; } = 2;

        // ROI autour de la dernière position (plus stable)
        public int RoiHalfSize { get; set; } = 160;

        // Pour éviter les faux lock: il faut X frames confirmées
        public int MinConfirmFrames { get; set; } = 2;

        private int _frameCount = 0;
        private int _confirm = 0;

        private Point _last = new Point(-1, -1);

        public void Reset()
        {
            _confirm = 0;
            _last = new Point(-1, -1);
            // On ne stop pas le template tracker ici pour ne pas casser tracking click,
            // c'est CameraView qui gère quel mode utiliser.
        }

        /// <summary>
        /// Essaie d'assurer un suivi (auto).
        /// Retourne true si position suivie dispo.
        /// </summary>
        public bool TryUpdate(byte[] rgb, int w, int h, Bitmap bmp24, out int x, out int y)
        {
            x = y = -1;
            if (!IsActive) return false;

            _frameCount++;

            // 1) Si template tracker suit déjà => meilleur signal
            if (_template.IsTracking)
            {
                if (_template.TryUpdate(rgb, w, h))
                {
                    _last = new Point(_template.X, _template.Y);
                    x = _template.X;
                    y = _template.Y;
                    _confirm = MinConfirmFrames;
                    return true;
                }

                // perdu => on laisse tomber le template
                _template.Stop();
                _confirm = 0;
            }

            // 2) Ré-acquisition via BallDetector (pas à chaque frame pour perf)
            if ((_frameCount % ReacquireEveryNFrames) != 0)
                return false;

            if (!TryDetectInRoi(bmp24, _last, out int cx, out int cy))
                return false;

            _confirm++;
            _last = new Point(cx, cy);

            // Quand confirmé assez de frames => start template tracking
            if (_confirm >= MinConfirmFrames)
            {
                // démarre template tracker sur image RGB (pas bitmap)
                bool started = _template.TryStart(rgb, w, h, cx, cy);
                if (started)
                {
                    x = cx; y = cy;
                    return true;
                }
                _confirm = 0;
            }

            // On a détecté, mais pas encore confirmé
            x = cx; y = cy;
            return true;
        }

        private bool TryDetectInRoi(Bitmap bmp24, Point last, out int cx, out int cy)
        {
            cx = cy = -1;

            // Pas de dernière position => détect global
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