using System;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Détecteur de rebond — machine d'état simple basée sur la vitesse verticale.
    ///
    /// PRINCIPE :
    ///   Phase 1 — FALLING  : la balle descend (dy > seuil) pendant MinFallFrames frames
    ///   Phase 2 — BOUNCED  : immédiatement après, la balle remonte (dy < -seuil) → REBOND ÉMIS
    ///
    ///   On ne cherche PAS à détecter le contact avec le sol.
    ///   On détecte l'inversion de vitesse, qui est le signal le plus fiable et le plus rapide.
    ///
    /// DEUX MODES :
    ///   Normal — tracker manuel, positions brutes
    ///   Smooth — tracker auto (positions lissées par template matching)
    ///            → seuils réduits car le lissage atténue les vitesses
    ///
    /// PARAMÈTRES ajustables :
    ///   MinFallFrames   — nb frames consécutives de descente avant d'accepter un rebond
    ///   FallSpeedPx     — vitesse min de descente (px/frame)
    ///   RiseSpeedPx     — vitesse min de remontée (px/frame) pour confirmer le rebond
    ///   CooldownMs      — délai minimum entre deux rebonds (évite doubles détections)
    /// </summary>
    public sealed class ImpactDetector
    {
        // ── Mode normal (tracker manuel) ─────────────────────────────────
        public float FallSpeedPx { get; set; } = 1.8f;
        public float RiseSpeedPx { get; set; } = 1.2f;
        public int MinFallFrames { get; set; } = 3;

        // ── Mode smooth (tracker auto — positions lissées) ────────────────
        public float FallSpeedPxSmooth { get; set; } = 0.6f;
        public float RiseSpeedPxSmooth { get; set; } = 0.5f;
        public int MinFallFramesSmooth { get; set; } = 4;

        public int CooldownMs { get; set; } = 400;

        // ── État ─────────────────────────────────────────────────────────
        private enum Phase { Idle, Falling }
        private Phase _phase = Phase.Idle;
        private int _fallCount = 0;
        private float _prevY = float.NaN;
        private long _lastFireTicks = 0;
        private bool _smoothMode = false;

        /// <summary>
        /// Appeler avec true quand le tracker auto est actif (positions lissées).
        /// Le changement de mode remet l'état à zéro.
        /// </summary>
        public void SetSmoothMode(bool smooth)
        {
            if (_smoothMode == smooth) return;
            _smoothMode = smooth;
            Reset();
        }

        public void Reset()
        {
            _phase = Phase.Idle;
            _fallCount = 0;
            _prevY = float.NaN;
            _lastFireTicks = 0;
        }

        /// <summary>
        /// Appeler chaque frame avec la position Y courante de la balle (bas de la balle = contactY).
        /// Retourne true exactement une fois par rebond détecté.
        /// </summary>
        public bool Update(float ballY, long nowTicks)
        {
            if (float.IsNaN(_prevY)) { _prevY = ballY; return false; }

            float dy = ballY - _prevY;   // positif = descend (Y augmente vers le bas)
            _prevY = ballY;

            float fallThresh = _smoothMode ? FallSpeedPxSmooth : FallSpeedPx;
            float riseThresh = _smoothMode ? RiseSpeedPxSmooth : RiseSpeedPx;
            int minFall = _smoothMode ? MinFallFramesSmooth : MinFallFrames;

            switch (_phase)
            {
                case Phase.Idle:
                    if (dy > fallThresh)
                    {
                        _fallCount++;
                        if (_fallCount >= minFall)
                            _phase = Phase.Falling;
                    }
                    else
                    {
                        _fallCount = 0;
                    }
                    break;

                case Phase.Falling:
                    if (dy < -riseThresh)
                    {
                        // Inversion confirmée → REBOND
                        _phase = Phase.Idle;
                        _fallCount = 0;
                        return TryFire(nowTicks);
                    }
                    else if (dy > fallThresh)
                    {
                        // Continue de descendre
                        _fallCount++;
                    }
                    else
                    {
                        // Vitesse nulle / incertaine — on reste Falling mais on reset le compteur
                        _fallCount = 0;
                    }
                    break;
            }

            return false;
        }

        // Ancienne API conservée pour compat
        public bool Update(bool clearlyInAir, bool contactGround, long nowTicks) => false;
        public bool UpdateAirToGround(bool airToContact, long nowTicks) => false;

        // ── Helper ───────────────────────────────────────────────────────

        private bool TryFire(long nowTicks)
        {
            long cd = CooldownMs * TimeSpan.TicksPerMillisecond;
            if (_lastFireTicks != 0 && (nowTicks - _lastFireTicks) < cd)
                return false;
            _lastFireTicks = nowTicks;
            return true;
        }
    }
}