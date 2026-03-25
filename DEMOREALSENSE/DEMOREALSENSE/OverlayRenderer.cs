using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DEMOREALSENSE
{
    public sealed class OverlayRenderer
    {
        public int ManualBoxHalf { get; set; } = 12;

        public void DrawManualBox(Bitmap bmp, int x, int y)
        {
            FrameBitmapConverter.DrawGreenBox(bmp, x, y, ManualBoxHalf);
        }

        public void DrawAutoCircle(Bitmap bmp, int x, int y, int radiusPx = 12)
        {
            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.DeepSkyBlue, 2f);
            g.DrawEllipse(pen, x - radiusPx, y - radiusPx, radiusPx * 2, radiusPx * 2);
        }

        public void DrawIaCircle(Bitmap bmp, int x, int y, int radiusPx = 16)
        {
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var pen = new Pen(Color.Magenta, 2.5f);
            g.DrawRectangle(pen, x - radiusPx, y - radiusPx, radiusPx * 2, radiusPx * 2);

            using var penCross = new Pen(Color.Magenta, 1.5f);
            g.DrawLine(penCross, x - 4, y, x + 4, y);
            g.DrawLine(penCross, x, y - 4, x, y + 4);
        }

        public void DrawGroundDebug(Bitmap bmp, float x, float yGround)
        {
            using var g = Graphics.FromImage(bmp);
            g.FillEllipse(Brushes.YellowGreen, x - 2, yGround - 2, 4, 4);
        }

        // ── API publique principale ───────────────────────────────────────

        /// <summary>
        /// Dessine la ligne sur la surface 3D réelle si le plan est disponible,
        /// sinon fallback 2D.
        ///
        /// PRINCIPE 3D :
        ///   La ligne 2D donne sa direction et ses extrémités dans l'image.
        ///   Pour chaque pixel X du segment, on calcule :
        ///     1. Le Y image de la surface via le plan 3D (perspective correcte)
        ///     2. La profondeur Z à ce point (depuis le plan 3D)
        ///     3. La largeur en pixels à cette profondeur (perspective correcte)
        ///   On obtient une polyligne qui "colle" visuellement à la table.
        ///
        /// La ligne n'est dessinée en 3D que si le plan est prêt (IsReady).
        /// Sinon, on dessine la ligne 2D classique.
        /// </summary>
        public void DrawLineOverlay3D(
            Bitmap bmp,
            ClickLineDetector lineDetector,
            object lineLock,
            float lineRealWidthMeters,
            TablePlaneDetector? plane,
            ushort[]? depth,
            float depthUnits)
        {
            bool hasLine;
            lock (lineLock)
            {
                hasLine = lineDetector.HasLine;
                if (!hasLine && lineDetector.Samples.Count == 0) return;
            }

            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Points cliqués (toujours affichés)
            lock (lineLock)
            {
                foreach (var p in lineDetector.Samples)
                    g.FillEllipse(Brushes.Lime, p.X - 3, p.Y - 3, 6, 6);
            }

            if (!hasLine) return;

            var bounds = new RectangleF(0, 0, bmp.Width - 1, bmp.Height - 1);
            if (!lineDetector.TryGetSegmentWithin(bounds, out var segA, out var segB))
                return;

            // ── Dessin 3D si plan disponible ──────────────────────────────
            if (plane != null && plane.IsReady && depth != null)
            {
                DrawPerspectiveLine(g, segA, segB, lineRealWidthMeters,
                                    plane, depth, depthUnits,
                                    bmp.Width, bmp.Height);
                return;
            }

            // ── Fallback 2D classique ─────────────────────────────────────
            Draw2DLine(g, segA, segB, 4f);
        }

        // ── Dessin en perspective sur la surface 3D ───────────────────────

        /// <summary>
        /// Dessine la ligne en perspective correcte sur la surface de la table.
        ///
        /// Pour chaque colonne X le long du segment :
        ///   - On récupère le Y sur le plan 3D (Y image réel de la surface)
        ///   - On calcule la profondeur Z en ce point depuis le plan
        ///   - On calcule la largeur en pixels à cette profondeur
        ///   - On construit les bords IN et OUT de la ligne
        /// Résultat : bande trapézoïdale qui suit la perspective de la table.
        /// </summary>
        private static void DrawPerspectiveLine(
            Graphics g,
            PointF segA, PointF segB,
            float lineRealWidthMeters,
            TablePlaneDetector plane,
            ushort[] depth,
            float depthUnits,
            int imgW, int imgH)
        {
            float dxSeg = segB.X - segA.X;
            float dySeg = segB.Y - segA.Y;
            float segLen = MathF.Sqrt(dxSeg * dxSeg + dySeg * dySeg);
            if (segLen < 2f) return;

            // Direction perpendiculaire normalisée (côté IN/OUT)
            float perpX = -dySeg / segLen;
            float perpY = dxSeg / segLen;

            // Nombre de samples — un tous les 2 pixels pour lisser
            int steps = Math.Max(2, (int)(segLen / 2f));

            var topPts = new List<PointF>(steps + 1); // côté IN
            var bottomPts = new List<PointF>(steps + 1); // côté OUT
            var centerPts = new List<PointF>(steps + 1); // axe central

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;

                // Position 2D interpolée le long du segment
                float px2d = segA.X + t * dxSeg;
                float py2d = segA.Y + t * dySeg;

                int ix = (int)Math.Clamp(px2d, 0, imgW - 1);

                // ── Y réel de la surface à ce X via le plan 3D ────────────
                // On calcule Z depuis le plan pour ce X pixel,
                // sans dépendre du depth buffer qui peut être bruité.
                float surfaceY = GetSurfaceYFromPlane(plane, ix, imgW, imgH,
                                                       depth, depthUnits, py2d);

                // ── Profondeur Z au point (ix, surfaceY) ──────────────────
                // On utilise le plan 3D pour calculer Z exact (pas le depth buffer)
                float Z = GetZFromPlane(plane, ix, (int)surfaceY, imgW, imgH);
                if (Z <= 0.05f) Z = 0.5f; // fallback

                // ── Largeur en pixels à cette profondeur ──────────────────
                // px = lineRealWidthMeters / (2 * Z * tan(hFov/2)) * imgW
                float pxPerMeter = imgW / (2f * Z * MathF.Tan(TablePlaneDetector.HFovRad / 2f));
                float halfW = lineRealWidthMeters * pxPerMeter / 2f;
                halfW = Math.Clamp(halfW, 1.5f, 30f); // clamp sécurité

                centerPts.Add(new PointF(px2d, surfaceY));
                topPts.Add(new PointF(px2d + perpX * halfW, surfaceY + perpY * halfW));
                bottomPts.Add(new PointF(px2d - perpX * halfW, surfaceY - perpY * halfW));
            }

            if (centerPts.Count < 2) return;

            // ── Construit le polygone de la bande ────────────────────────
            var poly = new PointF[centerPts.Count * 2];
            for (int i = 0; i < centerPts.Count; i++)
                poly[i] = topPts[i];
            for (int i = 0; i < centerPts.Count; i++)
                poly[centerPts.Count + i] = bottomPts[centerPts.Count - 1 - i];

            // Remplissage jaune semi-transparent
            using var fillBrush = new SolidBrush(Color.FromArgb(90, 255, 220, 0));
            g.FillPolygon(fillBrush, poly);

            // Bordure IN — blanc
            using var penIn = new Pen(Color.White, 1.5f);
            g.DrawLines(penIn, topPts.ToArray());

            // Bordure OUT — rouge
            using var penOut = new Pen(Color.Red, 1.5f);
            g.DrawLines(penOut, bottomPts.ToArray());

            // Axe central en tirets jaunes
            using var penCenter = new Pen(Color.Yellow, 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawLines(penCenter, centerPts.ToArray());
        }

        // ── Helpers plan 3D ───────────────────────────────────────────────

        /// <summary>
        /// Calcule le Y image de la surface au pixel X donné en utilisant le plan 3D.
        /// Stratégie : on cherche Z depuis le depth buffer autour de py2d,
        /// puis on intersecte le rayon avec le plan.
        /// Fallback = py2d si rien n'est disponible.
        /// </summary>
        private static float GetSurfaceYFromPlane(
            TablePlaneDetector plane,
            int ix, int imgW, int imgH,
            ushort[] depth, float depthUnits,
            float py2dFallback)
        {
            // Cherche une profondeur valide autour de py2d dans un rayon de 30px
            int py = (int)Math.Clamp(py2dFallback, 0, imgH - 1);
            float bestZ = 0f;

            for (int dy = -30; dy <= 30; dy += 5)
            {
                int scanY = py + dy;
                if (scanY < 0 || scanY >= imgH) continue;
                ushort raw = depth[scanY * imgW + ix];
                if (raw == 0) continue;
                float dm = raw * depthUnits;
                if (dm > 0.1f && dm < 4f) { bestZ = dm; break; }
            }

            if (bestZ <= 0f)
            {
                // Dernier recours : lit le depth buffer sur toute la colonne
                for (int scanY = imgH / 2; scanY < imgH; scanY++)
                {
                    ushort raw = depth[scanY * imgW + ix];
                    if (raw == 0) continue;
                    float dm = raw * depthUnits;
                    if (dm > 0.1f && dm < 4f) { bestZ = dm; break; }
                }
            }

            if (bestZ <= 0f) return py2dFallback;

            // Intersecte le rayon caméra avec le plan 3D
            // Rayon : direction = (angleH, angleV variable) à Z = bestZ
            float angleH = ((ix / (float)imgW) - 0.5f) * TablePlaneDetector.HFovRad;
            float X3d = bestZ * MathF.Tan(angleH);

            // Plan : A*X + B*Y + C*Z = D → cherche Y3d pour Z=bestZ, X=X3d
            if (MathF.Abs(plane.PlaneB) < 1e-6f) return py2dFallback;
            float Y3d = (plane.PlaneD - plane.PlaneA * X3d - plane.PlaneC * bestZ) / plane.PlaneB;

            // Reprojection Y3d → pixel
            float angleV = MathF.Atan2(Y3d, bestZ);
            float surfaceY = (angleV / TablePlaneDetector.VFovRad + 0.5f) * imgH;

            // Santé
            if (surfaceY < 0 || surfaceY > imgH) return py2dFallback;
            return surfaceY;
        }

        /// <summary>
        /// Calcule Z (profondeur en mètres) au pixel (ix, iy) sur le plan 3D.
        /// Résout le système : rayon caméra ∩ plan.
        /// </summary>
        private static float GetZFromPlane(
            TablePlaneDetector plane,
            int ix, int iy, int imgW, int imgH)
        {
            float angleH = ((ix / (float)imgW) - 0.5f) * TablePlaneDetector.HFovRad;
            float angleV = ((iy / (float)imgH) - 0.5f) * TablePlaneDetector.VFovRad;

            // Direction du rayon : (sin(H), sin(V), 1) normalisé
            float dx = MathF.Tan(angleH);
            float dy = MathF.Tan(angleV);
            // dz = 1 (rayon paramétrisé par Z)

            // Plan : A*(t*dx) + B*(t*dy) + C*t = D
            // t*(A*dx + B*dy + C) = D
            float denom = plane.PlaneA * dx + plane.PlaneB * dy + plane.PlaneC;
            if (MathF.Abs(denom) < 1e-6f) return 0f;

            float t = plane.PlaneD / denom;
            return t > 0f ? t : 0f; // t = Z
        }

        // ── Fallback 2D ───────────────────────────────────────────────────

        private static void Draw2DLine(Graphics g, PointF a, PointF b, float w)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1f) return;

            float nx = -dy / len * (w / 2f);
            float ny = dx / len * (w / 2f);

            var pts = new PointF[]
            {
                new PointF(a.X + nx, a.Y + ny),
                new PointF(b.X + nx, b.Y + ny),
                new PointF(b.X - nx, b.Y - ny),
                new PointF(a.X - nx, a.Y - ny),
            };

            using var fillBrush = new SolidBrush(Color.FromArgb(60, 255, 220, 0));
            g.FillPolygon(fillBrush, pts);

            using var penIn = new Pen(Color.White, 1.2f);
            using var penOut = new Pen(Color.Red, 1.2f);
            using var penC = new Pen(Color.Yellow, 1.5f) { DashStyle = DashStyle.Dash };

            g.DrawLine(penIn, new PointF(a.X + nx, a.Y + ny), new PointF(b.X + nx, b.Y + ny));
            g.DrawLine(penOut, new PointF(a.X - nx, a.Y - ny), new PointF(b.X - nx, b.Y - ny));
            g.DrawLine(penC, a, b);
        }

        // ── Court overlay ─────────────────────────────────────────────────

        public void DrawCourtOverlay(Bitmap bmp, CourtArea court)
        {
            if (court == null) return;
            var pts = court.Points;
            if (pts.Count == 0) return;

            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.Cyan, 2f);

            foreach (var p in pts)
                g.FillEllipse(Brushes.Cyan, p.X - 4, p.Y - 4, 8, 8);

            if (pts.Count >= 2)
            {
                for (int i = 0; i < pts.Count - 1; i++)
                    g.DrawLine(pen, pts[i], pts[i + 1]);
                if (court.HasCourt)
                    g.DrawLine(pen, pts[pts.Count - 1], pts[0]);
            }
        }
    }
}