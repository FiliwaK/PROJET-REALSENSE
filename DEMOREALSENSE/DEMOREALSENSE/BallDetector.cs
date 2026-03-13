using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Détecteur de balle basé sur HSV — robuste aux variations d'éclairage.
    /// Fonctionne parfaitement sur balle pickleball jaune vif.
    ///
    /// CALIBRATION : appelle CalibrateFromRgb(r, g, b) avec la couleur prélevée sur la balle.
    ///               Les tolérances HSV s'ajustent automatiquement.
    ///
    /// PARAMÈTRES clés :
    ///   HueTol        — tolérance teinte (±degrés). Jaune vif : 15-20 suffit.
    ///   SatMin        — saturation minimale (0-255). Évite les gris/blancs.
    ///   ValMin/ValMax — plage luminosité (0-255). Évite les zones noires ou brûlées.
    ///   MinBlobPixels — taille minimale blob détecté.
    ///   FillRatioMin  — circularité minimale (0=anything, 0.30=rondish).
    /// </summary>
    public sealed class BallDetector
    {
        // ── Paramètres HSV ──────────────────────────────────────────────
        public float TargetHue { get; set; } = 55f;   // jaune pickleball ~55°
        public float HueTol { get; set; } = 18f;   // ±18° autour de la teinte cible
        public byte SatMin { get; set; } = 100;   // saturation min (balle vive)
        public byte ValMin { get; set; } = 80;    // luminosité min
        public byte ValMax { get; set; } = 250;   // luminosité max (évite reflets brûlés)

        public int MinBlobPixels { get; set; } = 80;
        public float FillRatioMin { get; set; } = 0.28f;
        public float AspectMin { get; set; } = 0.40f;
        public float AspectMax { get; set; } = 2.50f;

        // ── Compatibilité ancienne API (ignorés, conservés pour ne pas casser l'existant) ──
        public byte TargetR { get; set; } = 220;
        public byte TargetG { get; set; } = 200;
        public byte TargetB { get; set; } = 40;
        public int TolR { get; set; } = 60;
        public int TolG { get; set; } = 60;
        public int TolB { get; set; } = 60;

        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Calibre automatiquement les paramètres HSV depuis une couleur RGB prélevée sur la balle.
        /// Appelé depuis CameraView lors d'un Shift+Clic.
        /// </summary>
        public void CalibrateFromRgb(byte r, byte g, byte b)
        {
            RgbToHsv(r, g, b, out float h, out float s, out float v);

            TargetHue = h;
            HueTol = 20f;   // large au départ — l'utilisateur peut affiner
            SatMin = (byte)Math.Max(60, (int)(s * 255f) - 60);
            ValMin = (byte)Math.Max(50, (int)(v * 255f) - 80);
            ValMax = (byte)Math.Min(255, (int)(v * 255f) + 60);

            // Mise à jour des champs RGB legacy pour compatibilité HUD
            TargetR = r; TargetG = g; TargetB = b;
        }

        // ────────────────────────────────────────────────────────────────

        public bool TryDetect(Bitmap bmp, out int cx, out int cy, out int approxRadius)
        {
            cx = cy = approxRadius = 0;

            if (bmp.PixelFormat != PixelFormat.Format24bppRgb)
            {
                using var tmp = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(tmp))
                    g.DrawImageUnscaled(bmp, 0, 0);
                return TryDetect(tmp, out cx, out cy, out approxRadius);
            }

            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                int w = bmp.Width;
                int h = bmp.Height;
                int stride = data.Stride;

                byte[] buf = new byte[stride * h];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                long sumX = 0, sumY = 0;
                int count = 0;
                int minX = w, minY = h, maxX = 0, maxY = 0;

                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = row + x * 3;

                        // BGR en mémoire Windows
                        byte bv = buf[i];
                        byte gv = buf[i + 1];
                        byte rv = buf[i + 2];

                        if (!MatchesHsv(rv, gv, bv)) continue;

                        count++;
                        sumX += x;
                        sumY += y;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }

                if (count < MinBlobPixels) return false;

                cx = (int)(sumX / count);
                cy = (int)(sumY / count);

                int bw = maxX - minX + 1;
                int bh = maxY - minY + 1;
                approxRadius = Math.Max(bw, bh) / 2;

                float fill = count / (float)(bw * bh);
                if (fill < FillRatioMin) return false;

                float aspect = bw / (float)Math.Max(1, bh);
                if (aspect < AspectMin || aspect > AspectMax) return false;

                return true;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private bool MatchesHsv(byte r, byte g, byte b)
        {
            RgbToHsv(r, g, b, out float h, out float s, out float v);

            byte sv = (byte)(s * 255f);
            byte vv = (byte)(v * 255f);

            if (sv < SatMin) return false;
            if (vv < ValMin) return false;
            if (vv > ValMax) return false;

            // Distance angulaire circulaire (0-360)
            float diff = Math.Abs(h - TargetHue);
            if (diff > 180f) diff = 360f - diff;

            return diff <= HueTol;
        }

        public static void RgbToHsv(byte r, byte g, byte b,
                                     out float h, out float s, out float v)
        {
            float rf = r / 255f;
            float gf = g / 255f;
            float bf = b / 255f;

            float max = Math.Max(rf, Math.Max(gf, bf));
            float min = Math.Min(rf, Math.Min(gf, bf));
            float diff = max - min;

            v = max;
            s = max < 1e-6f ? 0f : diff / max;

            if (diff < 1e-6f) { h = 0f; return; }

            if (max == rf) h = 60f * ((gf - bf) / diff % 6f);
            else if (max == gf) h = 60f * ((bf - rf) / diff + 2f);
            else h = 60f * ((rf - gf) / diff + 4f);

            if (h < 0f) h += 360f;
        }
    }
}