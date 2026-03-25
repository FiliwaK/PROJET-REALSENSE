using System;
using System.Collections.Generic;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Détecte automatiquement le plan 3D de la table depuis la profondeur RealSense.
    ///
    /// PRINCIPE :
    ///   On échantillonne la zone basse de l'image (là où est la table).
    ///   Pour chaque pixel on reconstruit sa position 3D (X,Y,Z) depuis la profondeur.
    ///   On fit un plan ax+by+cz=d par RANSAC pour ignorer les objets posés sur la table.
    ///
    ///   Une fois le plan connu :
    ///   - TryGetSurfaceY(px) retourne le vrai Y image de la surface à ce X pixel
    ///   - Permet de "coller" la ligne et les croix sur la surface réelle
    ///
    /// UTILISATION :
    ///   Appeler TryUpdate() chaque frame (throttlé par UpdateIntervalMs).
    ///   IsReady = true dès que le plan est fiable.
    /// </summary>
    public sealed class TablePlaneDetector
    {
        // ── Paramètres ────────────────────────────────────────────────────

        /// <summary>Fraction de l'image depuis le bas à analyser pour trouver la table.</summary>
        public float ScanBottomFraction { get; set; } = 0.55f;

        /// <summary>Fraction du haut de la zone de scan à ignorer (évite l'horizon).</summary>
        public float ScanTopSkipFraction { get; set; } = 0.10f;

        /// <summary>Pas d'échantillonnage en pixels.</summary>
        public int SampleStep { get; set; } = 12;

        /// <summary>Nombre d'itérations RANSAC.</summary>
        public int RansacIterations { get; set; } = 80;

        /// <summary>Distance max au plan pour être inlier (mètres).</summary>
        public float RansacInlierM { get; set; } = 0.015f;

        /// <summary>Nombre minimum d'inliers pour valider le plan.</summary>
        public int MinInliers { get; set; } = 40;

        /// <summary>Intervalle de mise à jour du plan (ms).</summary>
        public int UpdateIntervalMs { get; set; } = 2000;

        // ── Champ de vue caméra RealSense D435 ───────────────────────────
        public const float HFovRad = 69f * MathF.PI / 180f;
        public const float VFovRad = 42f * MathF.PI / 180f;

        // ── État ──────────────────────────────────────────────────────────

        public bool IsReady { get; private set; } = false;

        // Plan : normale (A,B,C) + D  tel que A*x + B*y + C*z = D
        public float PlaneA { get; private set; }
        public float PlaneB { get; private set; }
        public float PlaneC { get; private set; }
        public float PlaneD { get; private set; }

        private long _lastUpdateTicks = 0;
        private readonly Random _rng = new Random(42);

        // ── API principale ────────────────────────────────────────────────

        public void TryUpdate(ushort[] depth, int imgW, int imgH, float depthUnits, long nowTicks)
        {
            if (UpdateIntervalMs > 0)
            {
                long interval = UpdateIntervalMs * TimeSpan.TicksPerMillisecond;
                if (_lastUpdateTicks != 0 && (nowTicks - _lastUpdateTicks) < interval)
                    return;
            }
            _lastUpdateTicks = nowTicks;

            var pts = SamplePoints(depth, imgW, imgH, depthUnits);
            if (pts.Count < MinInliers) return;

            if (FitPlaneRansac(pts, out float a, out float b, out float c, out float d))
            {
                PlaneA = a; PlaneB = b; PlaneC = c; PlaneD = d;
                IsReady = true;
            }
        }

        public void Reset()
        {
            IsReady = false;
            _lastUpdateTicks = 0;
        }

        /// <summary>
        /// Pour un pixel image (imgX), retourne le Y image où se trouve
        /// la surface de la table à cet X, en utilisant le plan 3D détecté.
        ///
        /// refDepthM = profondeur de référence à cet X (en mètres).
        /// On utilise cette profondeur comme Z pour reconstruire le point 3D sur le plan.
        /// </summary>
        public bool TryGetSurfaceY(int imgX, int imgW, int imgH,
                                    float refDepthM, out float surfaceY)
        {
            surfaceY = 0f;
            if (!IsReady || refDepthM <= 0.05f) return false;

            // Angle horizontal du pixel imgX
            float angleH = ((imgX / (float)imgW) - 0.5f) * HFovRad;
            float Z = refDepthM;
            float X3d = Z * MathF.Tan(angleH);

            // Y 3D sur le plan : A*X + B*Y + C*Z = D → Y = (D - A*X - C*Z) / B
            if (MathF.Abs(PlaneB) < 1e-6f) return false;
            float Y3d = (PlaneD - PlaneA * X3d - PlaneC * Z) / PlaneB;

            // Reprojection Y3d → pixel Y image
            float angleV = MathF.Atan2(Y3d, Z);
            surfaceY = (angleV / VFovRad + 0.5f) * imgH;

            if (surfaceY < 0 || surfaceY >= imgH) return false;
            return true;
        }

        /// <summary>
        /// Retourne le Y image de la surface en échantillonnant la profondeur
        /// directement depuis le buffer depth au pixel (imgX, scanY).
        /// Plus précis que TryGetSurfaceY car utilise la vraie profondeur à cet X.
        /// </summary>
        public bool TryGetSurfaceYFromDepth(int imgX, int imgW, int imgH,
                                             ushort[] depth, float depthUnits,
                                             out float surfaceY)
        {
            surfaceY = 0f;
            if (!IsReady) return false;

            // Cherche une profondeur valide autour de imgX dans la zone basse
            int scanY = (int)(imgH * 0.75f);
            float bestDepth = 0f;

            for (int dy = -20; dy <= 20; dy += 4)
            {
                int py = scanY + dy;
                if (py < 0 || py >= imgH) continue;
                ushort raw = depth[py * imgW + imgX];
                if (raw == 0) continue;
                float dm = raw * depthUnits;
                if (dm > 0.1f && dm < 4f) { bestDepth = dm; break; }
            }

            if (bestDepth <= 0f) return false;
            return TryGetSurfaceY(imgX, imgW, imgH, bestDepth, out surfaceY);
        }

        // ── Helpers internes ──────────────────────────────────────────────

        private List<(float x, float y, float z)> SamplePoints(
            ushort[] depth, int imgW, int imgH, float depthUnits)
        {
            var pts = new List<(float, float, float)>(512);

            int yStart = (int)(imgH * (1f - ScanBottomFraction + ScanTopSkipFraction));
            int yEnd = imgH - 4;

            for (int py = yStart; py < yEnd; py += SampleStep)
            {
                for (int px = SampleStep; px < imgW - SampleStep; px += SampleStep)
                {
                    ushort raw = depth[py * imgW + px];
                    if (raw == 0) continue;

                    float Z = raw * depthUnits;
                    if (Z < 0.15f || Z > 3.5f) continue;

                    float angleH = ((px / (float)imgW) - 0.5f) * HFovRad;
                    float angleV = ((py / (float)imgH) - 0.5f) * VFovRad;

                    float X = Z * MathF.Tan(angleH);
                    float Y = Z * MathF.Tan(angleV);

                    pts.Add((X, Y, Z));
                }
            }

            return pts;
        }

        private bool FitPlaneRansac(
            List<(float x, float y, float z)> pts,
            out float bestA, out float bestB, out float bestC, out float bestD)
        {
            bestA = 0; bestB = 1; bestC = 0; bestD = 0;
            int bestCount = 0;
            int n = pts.Count;
            if (n < 3) return false;

            for (int iter = 0; iter < RansacIterations; iter++)
            {
                int i0 = _rng.Next(n), i1 = _rng.Next(n), i2 = _rng.Next(n);
                if (i0 == i1 || i1 == i2 || i0 == i2) continue;

                var (x0, y0, z0) = pts[i0];
                var (x1, y1, z1) = pts[i1];
                var (x2, y2, z2) = pts[i2];

                float ux = x1 - x0, uy = y1 - y0, uz = z1 - z0;
                float vx = x2 - x0, vy = y2 - y0, vz = z2 - z0;

                float nx = uy * vz - uz * vy;
                float ny = uz * vx - ux * vz;
                float nz = ux * vy - uy * vx;

                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-6f) continue;
                nx /= len; ny /= len; nz /= len;

                float d = nx * x0 + ny * y0 + nz * z0;

                int count = 0;
                foreach (var (px, py, pz) in pts)
                    if (MathF.Abs(nx * px + ny * py + nz * pz - d) < RansacInlierM) count++;

                if (count > bestCount)
                {
                    bestCount = count;
                    bestA = nx; bestB = ny; bestC = nz; bestD = d;
                }
            }

            if (bestCount < MinInliers) return false;
            RefitOnInliers(pts, ref bestA, ref bestB, ref bestC, ref bestD);
            return true;
        }

        private void RefitOnInliers(
            List<(float x, float y, float z)> pts,
            ref float a, ref float b, ref float c, ref float d)
        {
            var inliers = new List<(float x, float y, float z)>();
            foreach (var (px, py, pz) in pts)
                if (MathF.Abs(a * px + b * py + c * pz - d) < RansacInlierM)
                    inliers.Add((px, py, pz));

            if (inliers.Count < 4) return;

            float mx = 0, my = 0, mz = 0;
            foreach (var (px, py, pz) in inliers) { mx += px; my += py; mz += pz; }
            mx /= inliers.Count; my /= inliers.Count; mz /= inliers.Count;

            double sxx = 0, sxy = 0, sxz = 0, syy = 0, syz = 0, szz = 0;
            foreach (var (px, py, pz) in inliers)
            {
                double dx = px - mx, dy = py - my, dz = pz - mz;
                sxx += dx * dx; sxy += dx * dy; sxz += dx * dz;
                syy += dy * dy; syz += dy * dz; szz += dz * dz;
            }

            double na = a, nb = b, nc = c;
            for (int k = 0; k < 3; k++)
            {
                double ra = sxx * na + sxy * nb + sxz * nc;
                double rb = sxy * na + syy * nb + syz * nc;
                double rc = sxz * na + syz * nb + szz * nc;
                double rlen = Math.Sqrt(ra * ra + rb * rb + rc * rc);
                if (rlen < 1e-10) break;
                na = ra / rlen; nb = rb / rlen; nc = rc / rlen;
            }

            float flen = MathF.Sqrt((float)(na * na + nb * nb + nc * nc));
            if (flen < 1e-6f) return;

            a = (float)(na / flen); b = (float)(nb / flen); c = (float)(nc / flen);
            d = a * mx + b * my + c * mz;

            if (b > 0) { a = -a; b = -b; c = -c; d = -d; }
        }
    }
}