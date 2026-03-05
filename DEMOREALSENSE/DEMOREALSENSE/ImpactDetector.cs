using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class ImpactDetector
    {
        // --- Réglages (safe par défaut) ---
        public float MinPrevSpeed = 140f;        // vitesse avant impact
        public float MinSpeedDropRatio = 0.45f;  // speedNow < speedPrev * ratio
        public float MinVyFlip = 60f;            // vy change signe (rebond) avec amplitude

        public float MaxMoveAfterPx = 5f;        // déplacement faible => contact sol / roulage
        public int ConfirmFrames = 2;            // frames consécutives stables pour confirmer
        public int CandidateWindowFrames = 4;    // fenêtre max après candidat

        // --- Etat interne ---
        private bool _hasCandidate = false;
        private int _candidateAge = 0;
        private int _stableCount = 0;

        public void Reset()
        {
            _hasCandidate = false;
            _candidateAge = 0;
            _stableCount = 0;
        }

        /// <summary>
        /// Nouvelle API (VAR) : retourne true uniquement quand impact sol est confirmé.
        /// </summary>
        public bool Update(PointF vPrev, PointF vNow, float movePx)
        {
            float spPrev = Len(vPrev);
            float spNow = Len(vNow);

            if (_hasCandidate)
            {
                _candidateAge++;

                if (movePx <= MaxMoveAfterPx) _stableCount++;
                else _stableCount = 0;

                if (_stableCount >= ConfirmFrames)
                {
                    Reset();
                    return true; // IMPACT CONFIRME
                }

                if (_candidateAge > CandidateWindowFrames)
                    Reset();

                return false;
            }

            if (spPrev < MinPrevSpeed)
                return false;

            bool bigSpeedDrop = (spNow < spPrev * MinSpeedDropRatio);

            // Rebond vertical (descend puis remonte)
            // Note: Y image vers le bas => "descend" = vPrev.Y positif
            bool vyFlip = (vPrev.Y > +MinVyFlip && vNow.Y < -MinVyFlip);

            if (bigSpeedDrop || vyFlip)
            {
                _hasCandidate = true;
                _candidateAge = 0;
                _stableCount = (movePx <= MaxMoveAfterPx) ? 1 : 0;
            }

            return false;
        }

        /// <summary>
        /// COMPAT : ton CameraView appelle encore TryDetectFirstImpact(...)
        /// Donc on garde ce nom comme wrapper, sans casser ton code.
        /// </summary>
        public bool TryDetectFirstImpact(PointF vPrev, PointF vNow, float movePx)
            => Update(vPrev, vNow, movePx);

        private static float Len(PointF v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
    }
}