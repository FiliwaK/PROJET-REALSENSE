using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Interface commune pour les deux stratégies de détection.
    /// CameraPipeline appelle TryDetect() sans savoir si c'est algo ou IA.
    /// </summary>
    public interface IDetectionStrategy
    {
        /// <summary>
        /// Détecte la balle (et la ligne si mode IA) sur une frame.
        /// </summary>
        /// <param name="rgb">Buffer RGB brut de la caméra</param>
        /// <param name="bmp">Bitmap 24bpp correspondant (déjà converti)</param>
        /// <param name="w">Largeur image</param>
        /// <param name="h">Hauteur image</param>
        /// <returns>Résultat de détection</returns>
        DetectionResult Detect(byte[] rgb, Bitmap bmp, int w, int h);

        /// <summary>Remet à zéro l'état interne (tracker perdu, etc.).</summary>
        void Reset();
    }
}