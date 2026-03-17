using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Résultat de détection commun aux deux stratégies (algo et IA).
    /// CameraPipeline utilise ce résultat peu importe d'où il vient.
    /// </summary>
    public sealed class DetectionResult
    {
        /// <summary>Position centre balle détectée. Null si non détectée.</summary>
        public PointF? BallCenter { get; set; }

        /// <summary>Rayon estimé de la balle en pixels.</summary>
        public int BallRadius { get; set; } = 8;

        /// <summary>Confiance de la détection balle (0-1). 1.0 pour algo.</summary>
        public float BallConfidence { get; set; } = 1f;

        /// <summary>
        /// Ligne détectée par l'IA (segmentation).
        /// Null si pas de ligne ou si mode algo (la ligne algo est dans ClickLineDetector).
        /// </summary>
        public ClickLineDetector.LineModel? IaLineModel { get; set; }

        /// <summary>True si la ligne IA a été détectée cette frame.</summary>
        public bool HasIaLine => IaLineModel.HasValue;

        /// <summary>Source de la détection (pour affichage HUD).</summary>
        public DetectionMode Mode { get; set; } = DetectionMode.Algo;
    }

    public enum DetectionMode { Algo, Yolo }
}