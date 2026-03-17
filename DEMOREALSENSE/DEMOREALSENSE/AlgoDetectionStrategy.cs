using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Stratégie de détection algorithmique — utilise BallDetector + AutoTemplateFollower.
    /// C'est exactement ce qui tournait avant, juste encapsulé dans l'interface.
    /// La ligne reste gérée par ClickLineDetector (clics manuels Ctrl+Click).
    /// </summary>
    public sealed class AlgoDetectionStrategy : IDetectionStrategy
    {
        private readonly BallDetector _ballDetector;
        private readonly AutoTemplateFollower _autoFollower;

        public AlgoDetectionStrategy(
            BallDetector ballDetector,
            AutoTemplateFollower autoFollower)
        {
            _ballDetector = ballDetector;
            _autoFollower = autoFollower;
        }

        public DetectionResult Detect(byte[] rgb, Bitmap bmp, int w, int h)
        {
            var result = new DetectionResult { Mode = DetectionMode.Algo };

            bool ok = _autoFollower.TryUpdate(rgb, w, h, bmp, out int bx, out int by);

            if (ok && bx >= 0 && by >= 0)
            {
                result.BallCenter = new System.Drawing.PointF(bx, by);
                result.BallRadius = System.Math.Max(4, _autoFollower.LastRadius);
                result.BallConfidence = 1f;
            }

            return result;
        }

        public void Reset() => _autoFollower.Reset();
    }
}