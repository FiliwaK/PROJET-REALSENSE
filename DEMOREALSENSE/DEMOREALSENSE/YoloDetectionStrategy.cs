using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DEMOREALSENSE
{
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
        private const int TensorSize = 3 * ImgSize * ImgSize;

        // Tensor balle réutilisé — synchrone
        private readonly float[] _ballTensor = new float[TensorSize];

        // Ligne async + cache
        private readonly object _lineLock = new();
        private ClickLineDetector.LineModel? _cachedLine = null;
        private int _lineRunning = 0;
        private int _frameCount = 0;
        private readonly float[] _lineTensor = new float[TensorSize];

        public YoloDetectionStrategy(string ballOnnxPath, string lineOnnxPath,
                                     string? ballOpenVinoDir = null,
                                     string? lineOpenVinoDir = null)
        {
            _ballSession = CreateSession(ballOnnxPath, ballOpenVinoDir);
            _lineSession = CreateSession(lineOnnxPath, lineOpenVinoDir);
            _ballInputName = _ballSession.InputNames[0];
            _lineInputName = _lineSession.InputNames[0];
        }

        private static InferenceSession CreateSession(string onnxPath, string? openVinoDir)
        {
            // Tente OpenVINO GPU Intel
            if (!string.IsNullOrEmpty(openVinoDir) && Directory.Exists(openVinoDir))
            {
                var xmlFiles = Directory.GetFiles(openVinoDir, "*.xml");
                if (xmlFiles.Length > 0)
                {
                    try
                    {
                        var opts = new SessionOptions();
                        opts.AppendExecutionProvider_OpenVINO("GPU");
                        opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                        return new InferenceSession(onnxPath, opts);
                    }
                    catch { }
                }
            }

            // Fallback CPU optimisé
            var cpu = new SessionOptions();
            cpu.InterOpNumThreads = 2;
            cpu.IntraOpNumThreads = 2;
            cpu.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            cpu.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            return new InferenceSession(onnxPath, cpu);
        }

        public void Reset()
        {
            lock (_lineLock) _cachedLine = null;
            _frameCount = 0;
        }

        public DetectionResult Detect(byte[] rgb, Bitmap bmp, int w, int h)
        {
            var result = new DetectionResult { Mode = DetectionMode.Yolo };
            _frameCount++;

            // ── BALLE : synchrone sur frame courante ─────────────────────
            // Identique à la version originale qui marchait bien.
            // Pas d'async ici — précision maximale sur la frame courante.
            BuildTensor(rgb, w, h, _ballTensor);
            try
            {
                var box = RunBallDetect(_ballTensor);
                if (box.HasValue)
                {
                    float sx = w / (float)ImgSize;
                    float sy = h / (float)ImgSize;
                    result.BallCenter = new PointF(box.Value.cx * sx, box.Value.cy * sy);
                    result.BallRadius = Math.Max(4, (int)(Math.Max(box.Value.bw, box.Value.bh)
                                           * Math.Max(sx, sy) / 2f));
                    result.BallConfidence = box.Value.conf;
                }
            }
            catch { }

            // ── LIGNE : async toutes les N frames ────────────────────────
            if (_frameCount % LineEveryNFrames == 0 &&
                Interlocked.CompareExchange(ref _lineRunning, 1, 0) == 0)
            {
                var rgbCopy = new byte[rgb.Length];
                Buffer.BlockCopy(rgb, 0, rgbCopy, 0, rgb.Length);
                int capW = w, capH = h;

                Task.Run(() =>
                {
                    try
                    {
                        BuildTensor(rgbCopy, capW, capH, _lineTensor);
                        var line = RunLineFromBbox(_lineTensor, capW, capH);
                        if (line.HasValue)
                            lock (_lineLock) _cachedLine = line;
                    }
                    catch { }
                    finally { Interlocked.Exchange(ref _lineRunning, 0); }
                });
            }

            lock (_lineLock) result.IaLineModel = _cachedLine;
            return result;
        }

        // ── Inférence balle ──────────────────────────────────────────────

        private (float cx, float cy, float bw, float bh, float conf)? RunBallDetect(float[] tensor)
        {
            var input = new DenseTensor<float>(tensor, new[] { 1, 3, ImgSize, ImgSize });
            using var outputs = _ballSession.Run(new[] {
                NamedOnnxValue.CreateFromTensor(_ballInputName, input) });

            var raw = outputs[0].AsTensor<float>();
            int n = raw.Dimensions[2];
            float best = BallConfThresh;
            (float cx, float cy, float bw, float bh, float conf)? res = null;

            for (int i = 0; i < n; i++)
            {
                float c = raw[0, 4, i];
                if (c > best) { best = c; res = (raw[0, 0, i], raw[0, 1, i], raw[0, 2, i], raw[0, 3, i], c); }
            }
            return res;
        }

        // ── Ligne depuis bbox ─────────────────────────────────────────────

        private ClickLineDetector.LineModel? RunLineFromBbox(float[] tensor, int origW, int origH)
        {
            var input = new DenseTensor<float>(tensor, new[] { 1, 3, ImgSize, ImgSize });
            using var outputs = _lineSession.Run(new[] {
                NamedOnnxValue.CreateFromTensor(_lineInputName, input) });

            var boxes = outputs[0].AsTensor<float>();
            int n = boxes.Dimensions[2];
            float best = LineConfThresh;
            int bi = -1;

            for (int i = 0; i < n; i++)
            {
                float c = boxes[0, 4, i];
                if (c > best) { best = c; bi = i; }
            }
            if (bi < 0) return null;

            float sx = origW / (float)ImgSize;
            float sy = origH / (float)ImgSize;
            float cx = boxes[0, 0, bi] * sx;
            float cy = boxes[0, 1, bi] * sy;
            float bw = boxes[0, 2, bi] * sx;
            float bh = boxes[0, 3, bi] * sy;

            float topX = cx, topY = cy - bh / 2f;
            float botX = cx, botY = cy + bh / 2f;

            if (bw > bh * 0.7f)
            {
                topX = cx - bw / 2f; topY = cy;
                botX = cx + bw / 2f; botY = cy;
            }

            float dx = botX - topX;
            float dy = botY - topY;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 5f) return null;

            return new ClickLineDetector.LineModel(
                new PointF(cx, cy),
                new PointF(dx / len, dy / len));
        }

        // ── Tensor en place ───────────────────────────────────────────────

        private static void BuildTensor(byte[] rgb, int w, int h, float[] t)
        {
            float scaleX = w / (float)ImgSize;
            float scaleY = h / (float)ImgSize;

            for (int ty = 0; ty < ImgSize; ty++)
            {
                int sy = (int)(ty * scaleY);
                int row = sy * w * 3;
                int offR = ty * ImgSize;
                int offG = ImgSize * ImgSize + ty * ImgSize;
                int offB = 2 * ImgSize * ImgSize + ty * ImgSize;

                for (int tx = 0; tx < ImgSize; tx++)
                {
                    int si = row + (int)(tx * scaleX) * 3;
                    t[offR + tx] = rgb[si] / 255f;
                    t[offG + tx] = rgb[si + 1] / 255f;
                    t[offB + tx] = rgb[si + 2] / 255f;
                }
            }
        }

        public void Dispose()
        {
            _ballSession?.Dispose();
            _lineSession?.Dispose();
        }
    }
}