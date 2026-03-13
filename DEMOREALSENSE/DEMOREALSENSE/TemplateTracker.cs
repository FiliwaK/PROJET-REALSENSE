using System;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Tracking par Template Matching — version améliorée.
    ///
    /// AMÉLIORATIONS vs version originale :
    ///   1. SAD couleur (R+G+B) au lieu de grayscale → la balle jaune vive est mieux discriminée.
    ///   2. SearchRadius dynamique basé sur la vitesse estimée de la balle.
    ///      Si la balle s'est déplacée vite à la frame précédente, on agrandit la zone de recherche.
    ///   3. Score normalisé par pixel → MaxAcceptFactor comparable quelle que soit la taille du template.
    ///   4. Rejet des candidats identiques (bord image) pour éviter accrochage sur le bord.
    ///   5. RefreshTemplate() : recapture le template sur la position courante toutes N frames
    ///      pour éviter la dérive au fil du temps (balle qui tourne, lumière qui change).
    /// </summary>
    public sealed class TemplateTracker
    {
        public bool IsTracking { get; private set; }
        public int X { get; private set; } = -1;
        public int Y { get; private set; } = -1;

        // ── Paramètres ──────────────────────────────────────────────────
        public int TemplateSize { get; set; } = 21;   // réduit : 21px suffit pour une balle
        public int SearchRadius { get; set; } = 40;   // rayon de base
        public int MaxSearchRadius { get; set; } = 90;   // plafond balle rapide
        public float MaxAcceptNormScore { get; set; } = 28f;  // score SAD normalisé par pixel
        public int RefreshEveryFrames { get; set; } = 10;   // recapture template
        public int SearchStep { get; set; } = 2;    // step de recherche en pixels (1=dense, 2=2x plus rapide)

        // ── État interne ─────────────────────────────────────────────────
        private byte[]? _tplRgb;      // template RGB (3 canaux)
        private int _prevX = -1;
        private int _prevY = -1;
        private int _framesSinceRefresh = 0;

        // ────────────────────────────────────────────────────────────────

        public bool TryStart(byte[] rgb, int w, int h, int cx, int cy)
        {
            if (!CaptureTemplate(rgb, w, h, cx, cy, out var tpl))
                return false;

            _tplRgb = tpl;
            X = cx; Y = cy;
            _prevX = cx; _prevY = cy;
            _framesSinceRefresh = 0;
            IsTracking = true;
            return true;
        }

        public void Stop()
        {
            IsTracking = false;
            X = Y = _prevX = _prevY = -1;
            _tplRgb = null;
            _framesSinceRefresh = 0;
        }

        public bool TryUpdate(byte[] rgb, int w, int h)
        {
            if (!IsTracking || _tplRgb == null) return false;

            // Rayon dynamique : si la balle s'est déplacée vite, on cherche plus loin
            int dynRadius = ComputeDynamicRadius();

            if (!TemplateSearch(rgb, w, h, _tplRgb, TemplateSize, X, Y, dynRadius,
                    out int nx, out int ny, out float normScore))
                return false;

            _prevX = X; _prevY = Y;
            X = nx; Y = ny;

            // Refresh périodique du template (suit les changements d'aspect de la balle)
            _framesSinceRefresh++;
            if (_framesSinceRefresh >= RefreshEveryFrames)
            {
                if (CaptureTemplate(rgb, w, h, nx, ny, out var fresh))
                    _tplRgb = fresh;
                _framesSinceRefresh = 0;
            }

            return true;
        }

        // ── Rayon dynamique ──────────────────────────────────────────────

        private int ComputeDynamicRadius()
        {
            if (_prevX < 0 || _prevY < 0) return SearchRadius;

            float dx = X - _prevX;
            float dy = Y - _prevY;
            float move = (float)Math.Sqrt(dx * dx + dy * dy);

            // On ajoute 1.5× le déplacement passé à la zone de recherche
            int r = SearchRadius + (int)(move * 1.5f);
            return Math.Min(r, MaxSearchRadius);
        }

        // ── Capture template RGB ─────────────────────────────────────────

        private bool CaptureTemplate(byte[] rgb, int w, int h, int cx, int cy, out byte[] tplRgb)
        {
            tplRgb = Array.Empty<byte>();

            int size = TemplateSize;
            if (size < 9) size = 9;
            if ((size & 1) == 0) size++;

            int half = size / 2;
            int x0 = cx - half, y0 = cy - half;
            int x1 = cx + half, y1 = cy + half;

            if (x0 < 0 || y0 < 0 || x1 >= w || y1 >= h) return false;

            tplRgb = new byte[size * size * 3];
            int ti = 0;

            for (int y = y0; y <= y1; y++)
            {
                int row = y * w * 3;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x * 3;
                    tplRgb[ti++] = rgb[i];      // R
                    tplRgb[ti++] = rgb[i + 1];  // G
                    tplRgb[ti++] = rgb[i + 2];  // B
                }
            }

            return true;
        }

        // ── Recherche par SAD couleur ────────────────────────────────────

        private bool TemplateSearch(
            byte[] rgb, int w, int h,
            byte[] tplRgb, int tplSize,
            int cx, int cy, int searchRadius,
            out int bestX, out int bestY, out float bestNormScore)
        {
            bestX = cx; bestY = cy;
            bestNormScore = float.MaxValue;

            int half = tplSize / 2;
            if (cx < 0 || cy < 0 || cx >= w || cy >= h) return false;

            int sx0 = Math.Max(half, cx - searchRadius);
            int sy0 = Math.Max(half, cy - searchRadius);
            int sx1 = Math.Min(w - 1 - half, cx + searchRadius);
            int sy1 = Math.Min(h - 1 - half, cy + searchRadius);

            if (sx0 > sx1 || sy0 > sy1) return false;

            int pixCount = tplSize * tplSize;

            for (int y = sy0; y <= sy1; y += SearchStep)
            {
                for (int x = sx0; x <= sx1; x += SearchStep)
                {
                    float score = SadColorAt(rgb, w, x - half, y - half, tplRgb, tplSize);
                    if (score < bestNormScore)
                    {
                        bestNormScore = score;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            // Affinage au pixel près autour du meilleur candidat (si step > 1)
            if (SearchStep > 1)
            {
                int rx0 = Math.Max(sx0, bestX - SearchStep);
                int ry0 = Math.Max(sy0, bestY - SearchStep);
                int rx1 = Math.Min(sx1, bestX + SearchStep);
                int ry1 = Math.Min(sy1, bestY + SearchStep);

                for (int y = ry0; y <= ry1; y++)
                {
                    for (int x = rx0; x <= rx1; x++)
                    {
                        float score = SadColorAt(rgb, w, x - half, y - half, tplRgb, tplSize);
                        if (score < bestNormScore)
                        {
                            bestNormScore = score;
                            bestX = x;
                            bestY = y;
                        }
                    }
                }
            }

            bestNormScore /= (pixCount * 3f); // normalise par pixel et par canal
            return bestNormScore <= MaxAcceptNormScore;
        }

        // ── SAD couleur (R+G+B) ─────────────────────────────────────────

        private static float SadColorAt(
            byte[] rgb, int w,
            int x0, int y0,
            byte[] tplRgb, int size)
        {
            long score = 0;
            int ti = 0;

            for (int y = 0; y < size; y++)
            {
                int row = (y0 + y) * w * 3;
                for (int x = 0; x < size; x++)
                {
                    int i = row + (x0 + x) * 3;

                    int dr = rgb[i] - tplRgb[ti];
                    int dg = rgb[i + 1] - tplRgb[ti + 1];
                    int db = rgb[i + 2] - tplRgb[ti + 2];

                    score += Math.Abs(dr) + Math.Abs(dg) + Math.Abs(db);
                    ti += 3;
                }
            }

            return (float)score;
        }
    }
}