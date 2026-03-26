//using System;
//using System.Collections.Generic;
//using System.Drawing;

//namespace DEMOREALSENSE
//{
//    /// <summary>
//    /// Détecte le plan 3D de la table.
//    ///
//    /// SOLUTION A (Manuel) : Alt+Click x3 sur la table → plan exact immédiat.
//    /// SOLUTION B (Auto)   : RANSAC toutes les 1.5s, filtre plans verticaux.
//    ///
//    /// Les positions pixel des clics manuels sont stockées pour affichage.
//    /// </summary>
//    public sealed class TablePlaneDetector
//    {
//        public enum DetectionMode { Auto, Manual }
//        public DetectionMode Mode { get; set; } = DetectionMode.Auto;

//        // ── Paramètres RANSAC ─────────────────────────────────────────────
//        public float ScanBottomFraction { get; set; } = 0.55f;
//        public float ScanTopSkipFraction { get; set; } = 0.05f;
//        public int SampleStep { get; set; } = 10;
//        public int RansacIterations { get; set; } = 150;
//        public float RansacInlierM { get; set; } = 0.015f;
//        public int MinInliers { get; set; } = 25;

//        /// <summary>
//        /// Composante verticale minimale de la normale.
//        /// Table horizontale → |B| > 0.30.
//        /// Réduit à 0.30 pour être moins strict (était 0.35).
//        /// </summary>
//        public float MinHorizontalness { get; set; } = 0.30f;
//        public int UpdateIntervalMs { get; set; } = 1500;

//        // ── État ──────────────────────────────────────────────────────────
//        public bool IsReady { get; private set; } = false;
//        public float PlaneA { get; private set; }
//        public float PlaneB { get; private set; }
//        public float PlaneC { get; private set; }
//        public float PlaneD { get; private set; }

//        // Points 3D pour le calcul
//        private readonly List<(float x, float y, float z)> _manualPts3D = new();
//        // Points 2D pixel pour l'affichage visuel
//        private readonly List<PointF> _manualPts2D = new();

//        private long _lastUpdateTicks = 0;
//        private readonly Random _rng = new Random(42);

//        // ── Points de calibration visibles ───────────────────────────────
//        public IReadOnlyList<PointF> ManualPoints2D => _manualPts2D;
//        public int ManualPointCount => _manualPts3D.Count;

//        // ── Solution A : 3 clics ──────────────────────────────────────────

//        /// <summary>
//        /// Ajoute un point Alt+Click.
//        /// Stocke la position pixel (pour affichage) ET la position 3D (pour calcul).
//        /// Retourne true si le plan est calculé (3 points atteints).
//        /// </summary>
//        public bool AddManualPoint(int px, int py,
//                                    ushort[] depth, int imgW, int imgH,
//                                    CameraIntrinsics intr, float depthUnits)
//        {
//            // 4e clic → reset
//            if (_manualPts3D.Count >= 3)
//            {
//                _manualPts3D.Clear();
//                _manualPts2D.Clear();
//                IsReady = false;
//            }

//            // Profondeur au pixel cliqué (médiane 3x3)
//            float depthM = MedianDepth3x3(depth, imgW, imgH, px, py, depthUnits);
//            if (depthM <= 0.05f || depthM > 5f) return false;

//            // Stocke le pixel pour l'affichage
//            _manualPts2D.Add(new PointF(px, py));

//            // Déprojection 3D
//            if (!intr.DeprojectPixel(px, py, depthM, out float X, out float Y, out float Z))
//            {
//                // Fallback si intrinsèques invalides
//                float fx = imgW * 0.920f, fy = imgH * 1.165f;
//                float cx2 = imgW / 2f, cy2 = imgH / 2f;
//                X = (px - cx2) / fx * depthM;
//                Y = (py - cy2) / fy * depthM;
//                Z = depthM;
//            }

//            _manualPts3D.Add((X, Y, Z));

//            if (_manualPts3D.Count == 3)
//            {
//                if (ComputePlaneFrom3Points(_manualPts3D[0], _manualPts3D[1], _manualPts3D[2],
//                        out float a, out float b, out float c, out float d))
//                {
//                    PlaneA = a; PlaneB = b; PlaneC = c; PlaneD = d;
//                    IsReady = true;
//                    return true;
//                }
//            }
//            return false;
//        }

//        public void ResetManual()
//        {
//            _manualPts3D.Clear();
//            _manualPts2D.Clear();
//            if (Mode == DetectionMode.Manual) IsReady = false;
//        }

//        // ── Solution B : RANSAC ───────────────────────────────────────────

//        public void TryUpdate(ushort[] depth, int imgW, int imgH,
//                               CameraIntrinsics intr, float depthUnits, long nowTicks)
//        {
//            if (Mode != DetectionMode.Auto) return;

//            if (UpdateIntervalMs > 0)
//            {
//                long interval = UpdateIntervalMs * TimeSpan.TicksPerMillisecond;
//                if (_lastUpdateTicks != 0 && (nowTicks - _lastUpdateTicks) < interval) return;
//            }
//            _lastUpdateTicks = nowTicks;

//            var pts = SamplePoints(depth, imgW, imgH, intr, depthUnits);
//            if (pts.Count < MinInliers) return;

//            if (FitPlaneRansac(pts, out float a, out float b, out float c, out float d))
//            {
//                PlaneA = a; PlaneB = b; PlaneC = c; PlaneD = d;
//                IsReady = true;
//            }
//        }

//        // Surcharge sans intrinsèques (approximation FOV)
//        public void TryUpdate(ushort[] depth, int imgW, int imgH,
//                               float depthUnits, long nowTicks)
//        {
//            var fallback = new CameraIntrinsics
//            {
//                Width = imgW,
//                Height = imgH,
//                Fx = imgW * 0.920f,
//                Fy = imgH * 1.165f,
//                Cx = imgW / 2f,
//                Cy = imgH / 2f,
//            };
//            TryUpdate(depth, imgW, imgH, fallback, depthUnits, nowTicks);
//        }

//        public void Reset()
//        {
//            IsReady = false;
//            _lastUpdateTicks = 0;
//            _manualPts3D.Clear();
//            _manualPts2D.Clear();
//        }

//        // ── Intersection rayon-plan ───────────────────────────────────────

//        public bool DeprojectPixelToPlane(float px, float py, CameraIntrinsics intr,
//                                           out float X3d, out float Y3d, out float Z3d)
//        {
//            X3d = Y3d = Z3d = 0f;
//            if (!IsReady) return false;

//            float fx = intr.IsValid ? intr.Fx : 1f;
//            float fy = intr.IsValid ? intr.Fy : 1f;
//            float cx = intr.IsValid ? intr.Cx : px;
//            float cy = intr.IsValid ? intr.Cy : py;

//            float dx = (px - cx) / fx;
//            float dy = (py - cy) / fy;

//            float denom = PlaneA * dx + PlaneB * dy + PlaneC;
//            if (MathF.Abs(denom) < 1e-6f) return false;

//            float t = PlaneD / denom;
//            if (t <= 0.05f || t > 10f) return false;

//            Z3d = t; X3d = t * dx; Y3d = t * dy;
//            return true;
//        }

//        public static bool ProjectToPixel(float X3d, float Y3d, float Z3d,
//                                           CameraIntrinsics intr,
//                                           out float px, out float py)
//        {
//            return intr.ProjectPoint(X3d, Y3d, Z3d, out px, out py);
//        }

//        // ── Helpers ───────────────────────────────────────────────────────

//        private static bool ComputePlaneFrom3Points(
//            (float x, float y, float z) p0,
//            (float x, float y, float z) p1,
//            (float x, float y, float z) p2,
//            out float a, out float b, out float c, out float d)
//        {
//            a = b = c = d = 0f;
//            float ux = p1.x - p0.x, uy = p1.y - p0.y, uz = p1.z - p0.z;
//            float vx = p2.x - p0.x, vy = p2.y - p0.y, vz = p2.z - p0.z;
//            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
//            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
//            if (len < 1e-6f) return false;
//            a = nx / len; b = ny / len; c = nz / len;
//            d = a * p0.x + b * p0.y + c * p0.z;
//            if (b < 0) { a = -a; b = -b; c = -c; d = -d; }
//            return true;
//        }

//        private static float MedianDepth3x3(ushort[] depth, int imgW, int imgH,
//                                              int cx, int cy, float depthUnits)
//        {
//            var vals = new List<float>(9);
//            for (int dy = -1; dy <= 1; dy++)
//                for (int dx = -1; dx <= 1; dx++)
//                {
//                    int x = cx + dx, y = cy + dy;
//                    if (x < 0 || x >= imgW || y < 0 || y >= imgH) continue;
//                    ushort raw = depth[y * imgW + x];
//                    if (raw == 0) continue;
//                    vals.Add(raw * depthUnits);
//                }
//            if (vals.Count == 0) return 0f;
//            vals.Sort();
//            return vals[vals.Count / 2];
//        }

//        private List<(float x, float y, float z)> SamplePoints(
//            ushort[] depth, int imgW, int imgH,
//            CameraIntrinsics intr, float depthUnits)
//        {
//            var pts = new List<(float, float, float)>(512);
//            int yStart = (int)(imgH * (1f - ScanBottomFraction + ScanTopSkipFraction));
//            int yEnd = imgH - 2;

//            for (int py = yStart; py < yEnd; py += SampleStep)
//                for (int px = SampleStep; px < imgW - SampleStep; px += SampleStep)
//                {
//                    ushort raw = depth[py * imgW + px];
//                    if (raw == 0) continue;
//                    float Z = raw * depthUnits;
//                    if (Z < 0.15f || Z > 3.5f) continue;

//                    float X, Y;
//                    if (intr.IsValid)
//                    {
//                        X = (px - intr.Cx) / intr.Fx * Z;
//                        Y = (py - intr.Cy) / intr.Fy * Z;
//                    }
//                    else
//                    {
//                        X = (px - imgW / 2f) / (imgW * 0.920f) * Z;
//                        Y = (py - imgH / 2f) / (imgH * 1.165f) * Z;
//                    }
//                    pts.Add((X, Y, Z));
//                }
//            return pts;
//        }

//        private bool FitPlaneRansac(
//            List<(float x, float y, float z)> pts,
//            out float bestA, out float bestB, out float bestC, out float bestD)
//        {
//            bestA = 0; bestB = 1; bestC = 0; bestD = 0;
//            int bestCount = 0, n = pts.Count;
//            if (n < 3) return false;

//            for (int iter = 0; iter < RansacIterations; iter++)
//            {
//                int i0 = _rng.Next(n), i1 = _rng.Next(n), i2 = _rng.Next(n);
//                if (i0 == i1 || i1 == i2 || i0 == i2) continue;

//                if (!ComputePlaneFrom3Points(pts[i0], pts[i1], pts[i2],
//                        out float nx, out float ny, out float nz, out float d)) continue;

//                // Rejette les plans verticaux
//                if (MathF.Abs(ny) < MinHorizontalness) continue;

//                int count = 0;
//                foreach (var p in pts)
//                    if (MathF.Abs(nx * p.x + ny * p.y + nz * p.z - d) < RansacInlierM) count++;

//                if (count > bestCount)
//                { bestCount = count; bestA = nx; bestB = ny; bestC = nz; bestD = d; }
//            }

//            if (bestCount < MinInliers) return false;
//            RefitOnInliers(pts, ref bestA, ref bestB, ref bestC, ref bestD);
//            if (bestB < 0) { bestA = -bestA; bestB = -bestB; bestC = -bestC; bestD = -bestD; }
//            return true;
//        }

//        private void RefitOnInliers(List<(float x, float y, float z)> pts,
//                                     ref float a, ref float b, ref float c, ref float d)
//        {
//            var inliers = new List<(float x, float y, float z)>();
//            foreach (var p in pts)
//                if (MathF.Abs(a * p.x + b * p.y + c * p.z - d) < RansacInlierM * 2f) inliers.Add(p);
//            if (inliers.Count < 4) return;

//            float mx = 0, my = 0, mz = 0;
//            foreach (var p in inliers) { mx += p.x; my += p.y; mz += p.z; }
//            mx /= inliers.Count; my /= inliers.Count; mz /= inliers.Count;

//            double sxx = 0, sxy = 0, sxz = 0, syy = 0, syz = 0, szz = 0;
//            foreach (var p in inliers)
//            {
//                double dx = p.x - mx, dy = p.y - my, dz = p.z - mz;
//                sxx += dx * dx; sxy += dx * dy; sxz += dx * dz; syy += dy * dy; syz += dy * dz; szz += dz * dz;
//            }
//            double na = a, nb = b, nc = c;
//            for (int k = 0; k < 5; k++)
//            {
//                double ra = sxx * na + sxy * nb + sxz * nc;
//                double rb = sxy * na + syy * nb + syz * nc;
//                double rc = sxz * na + syz * nb + szz * nc;
//                double rlen = Math.Sqrt(ra * ra + rb * rb + rc * rc);
//                if (rlen < 1e-10) break;
//                na = ra / rlen; nb = rb / rlen; nc = rc / rlen;
//            }
//            float flen = MathF.Sqrt((float)(na * na + nb * nb + nc * nc));
//            if (flen < 1e-6f) return;
//            a = (float)(na / flen); b = (float)(nb / flen); c = (float)(nc / flen);
//            d = a * mx + b * my + c * mz;
//        }
//    }
//}