using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Détecte automatiquement via BallDetector puis démarre TemplateTracker (stable).
    /// Ré-acquiert quand perdu. Vérifie périodiquement pour éviter dérive.
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

        public int RoiHalfSize { get; set; } = 240;

        /// <summary>Quand on a une dernière position, reacquire par ROI toutes N frames.</summary>
        public int ReacquireEveryNFrames { get; set; } = 2;

        /// <summary>Quand on n’a aucune position, full-frame toutes N frames.</summary>
        public int ReacquireEveryNFramesWhenUnknown { get; set; } = 2;

        /// <summary>Nombre de détections consécutives avant de lock template.</summary>
        public int MinConfirmFrames { get; set; } = 2;

        /// <summary>Vérifie la dérive du template toutes N frames (ROI detector).</summary>
        public int VerifyEveryNFrames { get; set; } = 4;

        /// <summary>Si le detector trouve un centre loin du template => stop et reacquire.</summary>
        public float MaxDriftPx { get; set; } = 30f;

        private int _frameCount = 0;
        private int _confirm = 0;
        private Point _last = new Point(-1, -1);

        public void Reset()
        {
            _confirm = 0;
            _last = new Point(-1, -1);
        }

        public bool TryUpdate(byte[] rgb, int w, int h, Bitmap bmp24, out int bx, out int by)
        {
            bx = by = -1;
            if (rgb == null || bmp24 == null) return false;

            _frameCount++;

            // 1) Si TemplateTracker actif, on suit
            if (_template.IsTracking)
            {
                if (_template.TryUpdate(rgb, w, h))
                {
                    int tx = _template.X;
                    int ty = _template.Y;
                    _last = new Point(tx, ty);
                    bx = tx; by = ty;

                    // Vérif anti-dérive périodique
                    if (VerifyEveryNFrames > 0 && (_frameCount % VerifyEveryNFrames) == 0)
                    {
                        if (TryDetectInRoi(bmp24, _last, out int cx, out int cy))
                        {
                            float d = Dist(tx, ty, cx, cy);
                            if (d > MaxDriftPx)
                            {
                                _template.Stop();
                                _confirm = 0;
                                // on renvoie la mesure detector (plus fiable) pour relocker vite
                                _last = new Point(cx, cy);
                                bx = cx; by = cy;
                                return true;
                            }
                        }
                    }

                    return true;
                }

                // perdu -> stop
                _template.Stop();
                _confirm = 0;
            }

            // 2) Réacquisition
            bool hasLast = _last.X >= 0 && _last.Y >= 0;

            int rate = hasLast ? ReacquireEveryNFrames : ReacquireEveryNFramesWhenUnknown;
            rate = Math.Max(1, rate);

            if ((_frameCount % rate) != 0)
                return false;

            if (!TryDetectInRoi(bmp24, _last, out int dx, out int dy))
                return false;

            _confirm++;
            _last = new Point(dx, dy);
            bx = dx; by = dy;

            if (_confirm >= MinConfirmFrames)
            {
                if (_template.TryStart(rgb, w, h, dx, dy))
                {
                    _confirm = MinConfirmFrames;
                    return true;
                }
                _confirm = 0;
            }

            return true;
        }

        private bool TryDetectInRoi(Bitmap bmp24, Point last, out int cx, out int cy)
        {
            cx = cy = -1;

            // Pas de last => full frame
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

        private static float Dist(int ax, int ay, int bx, int by)
        {
            float dx = ax - bx;
            float dy = ay - by;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}