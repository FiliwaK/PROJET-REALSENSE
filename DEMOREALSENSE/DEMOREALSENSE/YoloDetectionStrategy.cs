using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Stratégie IA YOLO11.
    ///
    /// BALLE : yolo11n detect  → bounding box centre → chaque frame
    /// LIGNE : yolo11n-seg     → bounding box de la détection → axe médian vertical
    ///
    /// POURQUOI bbox et pas masque :
    ///   Le masque pixel par pixel est lent (~200ms) et sensible au bruit.
    ///   La bounding box de YOLO est calculée gratuitement pendant l'inférence.
    ///   La ligne jaune est une bande verticale/diagonale → son axe médian
    ///   = droite passant par le centre haut et centre bas de la bbox.
    ///   C'est exact, rapide, et insensible à la balle.
    /// </summary>
    public sealed class YoloDetectionStrategy : IDetectionStrategy, IDisposable
    {
        public float BallConfThresh { get; set; } = 0.30f;
        public float LineConfThresh { get; set; } = 0.25f;
        public int LineEveryNFrames { get; set; } = 8;

        private readonly InferenceSession _ballSession;
        private readonly InferenceSession _lineSession;
        private readonly string _ballInputName;
        private readonly string _lineInputName;

        private const int ImgSize = 640;

        private ClickLineDetector.LineModel? _cachedLine = null;
        private int _frameCount = 0;

        public YoloDetectionStrategy(string ballOnnxPath, string lineOnnxPath)
        {
            var opts = new SessionOptions();
            opts.InterOpNumThreads = 2;
            opts.IntraOpNumThreads = 2;
            opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            opts.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;

            _ballSession = new InferenceSession(ballOnnxPath, opts);
            _lineSession = new InferenceSession(lineOnnxPath, opts);
            _ballInputName = _ballSession.InputNames[0];
            _lineInputName = _lineSession.InputNames[0];
        }

        public void Reset()
        {
            _cachedLine = null;
            _frameCount = 0;
        }

        public DetectionResult Detect(byte[] rgb, Bitmap bmp, int w, int h)
        {
            var result = new DetectionResult { Mode = DetectionMode.Yolo };
            _frameCount++;

            float[] tensor = BuildTensor(rgb, w, h);

            // ── Balle : chaque frame ─────────────────────────────────────
            try
            {
                var box = RunBallDetect(tensor);
                if (box.HasValue)
                {
                    float sx = w / (float)ImgSize;
                    float sy = h / (float)ImgSize;
                    result.BallCenter = new PointF(box.Value.cx * sx, box.Value.cy * sy);
                    result.BallRadius = (int)(Math.Max(box.Value.bw, box.Value.bh)
                                           * Math.Max(sx, sy) / 2f);
                    result.BallConfidence = box.Value.conf;
                }
            }
            catch { }

            // ── Ligne : throttlée, depuis bbox uniquement ────────────────
            if (_frameCount % LineEveryNFrames == 0)
            {
                try
                {
                    var line = RunLineFromBbox(tensor, w, h);
                    if (line.HasValue) _cachedLine = line;
                }
                catch { }
            }

            result.IaLineModel = _cachedLine;
            return result;
        }

        // ── Détection balle ──────────────────────────────────────────────

        private (float cx, float cy, float bw, float bh, float conf)? RunBallDetect(float[] tensor)
        {
            var input = new DenseTensor<float>(tensor, new[] { 1, 3, ImgSize, ImgSize });
            using var outputs = _ballSession.Run(new[] {
                NamedOnnxValue.CreateFromTensor(_ballInputName, input) });

            var raw = outputs[0].AsTensor<float>();
            int numDet = raw.Dimensions[2];
            float bestC = BallConfThresh;
            (float cx, float cy, float bw, float bh, float conf)? best = null;

            for (int i = 0; i < numDet; i++)
            {
                float c = raw[0, 4, i];
                if (c > bestC)
                {
                    bestC = c;
                    best = (raw[0, 0, i], raw[0, 1, i], raw[0, 2, i], raw[0, 3, i], c);
                }
            }
            return best;
        }

        // ── Ligne depuis bounding box ────────────────────────────────────

        /// <summary>
        /// Extrait la ligne depuis la bounding box de la segmentation.
        ///
        /// La ligne jaune est une bande verticale/diagonale dans l'image.
        /// Sa bbox YOLO = (cx, cy, bw, bh) en coordonnées 640×640.
        ///
        /// On construit le LineModel en passant par :
        ///   - Point haut  = (cx, cy - bh/2)  → sommet de la bbox
        ///   - Point bas   = (cx, cy + bh/2)  → bas de la bbox
        ///   Direction = vecteur normalisé haut→bas reprojeté en coords image.
        ///
        /// C'est beaucoup plus stable que le masque et instantané.
        /// </summary>
        private ClickLineDetector.LineModel? RunLineFromBbox(float[] tensor, int origW, int origH)
        {
            // On n'a besoin que de la sortie boxes, pas des protos
            // → on lance quand même les deux sorties car YOLO-seg les produit ensemble
            var input = new DenseTensor<float>(tensor, new[] { 1, 3, ImgSize, ImgSize });
            using var outputs = _lineSession.Run(new[] {
                NamedOnnxValue.CreateFromTensor(_lineInputName, input) });

            var boxes = outputs[0].AsTensor<float>(); // (1, 37, 8400)
            int numDet = boxes.Dimensions[2];

            float bestC = LineConfThresh;
            int bestI = -1;
            for (int i = 0; i < numDet; i++)
            {
                float c = boxes[0, 4, i];
                if (c > bestC) { bestC = c; bestI = i; }
            }
            if (bestI < 0) return null;

            // Coordonnées bbox en espace 640×640
            float cx640 = boxes[0, 0, bestI];
            float cy640 = boxes[0, 1, bestI];
            float bw640 = boxes[0, 2, bestI];
            float bh640 = boxes[0, 3, bestI];

            // Reprojection vers image originale
            float sx = origW / (float)ImgSize;
            float sy = origH / (float)ImgSize;

            float cx = cx640 * sx;
            float cy = cy640 * sy;
            float bh = bh640 * sy;
            float bw = bw640 * sx;

            // Points haut et bas de l'axe médian de la bbox
            // Pour une ligne diagonale : on utilise les coins de la bbox
            // Point du haut = centre haut de la bbox
            float topX = cx;
            float topY = cy - bh / 2f;

            // Point du bas = centre bas de la bbox
            float botX = cx;
            float botY = cy + bh / 2f;

            // Si la bbox est plus large que haute (ligne très inclinée)
            // on utilise les coins gauche/droite à mi-hauteur
            if (bw > bh * 0.7f)
            {
                topX = cx - bw / 2f;
                topY = cy;
                botX = cx + bw / 2f;
                botY = cy;
            }

            // Vecteur direction
            float dx = botX - topX;
            float dy = botY - topY;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 5f) return null;

            // Point de référence = centre de la bbox
            var linePoint = new PointF(cx, cy);
            var lineDir = new PointF(dx / len, dy / len);

            return new ClickLineDetector.LineModel(linePoint, lineDir);
        }

        // ── Tensor ───────────────────────────────────────────────────────

        private static float[] BuildTensor(byte[] rgb, int w, int h)
        {
            float[] t = new float[3 * ImgSize * ImgSize];
            float scaleX = w / (float)ImgSize;
            float scaleY = h / (float)ImgSize;

            for (int ty = 0; ty < ImgSize; ty++)
            {
                int sy = (int)(ty * scaleY);
                int row = sy * w * 3;
                for (int tx = 0; tx < ImgSize; tx++)
                {
                    int si = row + (int)(tx * scaleX) * 3;
                    t[0 * ImgSize * ImgSize + ty * ImgSize + tx] = rgb[si] / 255f;
                    t[1 * ImgSize * ImgSize + ty * ImgSize + tx] = rgb[si + 1] / 255f;
                    t[2 * ImgSize * ImgSize + ty * ImgSize + tx] = rgb[si + 2] / 255f;
                }
            }
            return t;
        }

        public void Dispose()
        {
            _ballSession?.Dispose();
            _lineSession?.Dispose();
        }
    }
}