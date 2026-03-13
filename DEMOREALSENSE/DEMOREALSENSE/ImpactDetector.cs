using System;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Détecteur de rebond par INVERSION DE VITESSE VERTICALE.
    ///
    /// PRINCIPE :
    ///   Un rebond = la balle descend (dy > 0 en image) PUIS remonte (dy < 0).
    ///   Détection par comparaison de la vitesse moyenne sur deux demi-fenêtres.
    ///
    /// DEUX MODES :
    ///   Mode normal  — pour tracker manuel ou positions brutes (seuils standard)
    ///   Mode smooth  — pour tracker auto qui lisse les positions (seuils réduits)
    ///   → AppellerSetSmoothMode(true) quand le tracker auto est actif.
    ///
    /// PARAMÈTRES :
    ///   MinFallSpeedPx / MinRiseSpeedPx — seuils de vitesse (px/frame)
    ///   CooldownMs     — délai min entre deux rebonds
    ///   HistoryFrames  — fenêtre glissante (frames mémorisées)
    /// </summary>
    public sealed class ImpactDetector
    {
        // ── Paramètres mode normal ───────────────────────────────────────
        public float MinFallSpeedPx { get; set; } = 1.5f;
        public float MinRiseSpeedPx { get; set; } = 1.0f;

        // ── Paramètres mode smooth (tracker auto) ────────────────────────
        public float MinFallSpeedPxSmooth { get; set; } = 0.5f;  // lissé → seuil très bas
        public float MinRiseSpeedPxSmooth { get; set; } = 0.4f;

        public int CooldownMs { get; set; } = 350;
        public int HistoryFrames { get; set; } = 6;  // fenêtre plus large = plus robuste

        // ── Etat ─────────────────────────────────────────────────────────
        private readonly float[] _yHistory = new float[32];
        private int _head = 0;
        private int _count = 0;
        private bool _wasFalling = false;
        private long _lastImpactTicks = 0;
        private bool _smoothMode = false;

        /// <summary>
        /// Appeler avec true quand le tracker auto est actif (positions lissées).
        /// Appeler avec false pour le tracker manuel.
        /// </summary>
        public void SetSmoothMode(bool smooth)
        {
            if (_smoothMode != smooth)
            {
                _smoothMode = smooth;
                // Reset l'historique car les échelles de mouvement changent
                Reset();
            }
        }

        public void Reset()
        {
            _head = 0;
            _count = 0;
            _wasFalling = false;
            _lastImpactTicks = 0;
            Array.Clear(_yHistory, 0, _yHistory.Length);
        }

        /// <summary>
        /// Appeler chaque frame avec la position Y de la balle (pixels).
        /// Retourne true au moment exact du rebond (inversion descente→montée).
        /// </summary>
        public bool Update(float ballY, long nowTicks)
        {
            _yHistory[_head % _yHistory.Length] = ballY;
            _head++;
            if (_count < _yHistory.Length) _count++;

            if (_count < HistoryFrames) return false;

            int half = HistoryFrames / 2;
            float dyRecent = AverageDy(0, half);            // frames récentes
            float dyOlder = AverageDy(half, HistoryFrames);   // frames précédentes

            float fallThresh = _smoothMode ? MinFallSpeedPxSmooth : MinFallSpeedPx;
            float riseThresh = _smoothMode ? MinRiseSpeedPxSmooth : MinRiseSpeedPx;

            bool fallingNow = dyOlder > fallThresh;
            bool risingNow = dyRecent < -riseThresh;

            if (_wasFalling && risingNow)
            {
                _wasFalling = false;
                return TryFireImpact(nowTicks);
            }

            if (fallingNow && !risingNow) _wasFalling = true;
            if (risingNow) _wasFalling = false;

            return false;
        }

        // Ancienne API conservée pour compat
        public bool Update(bool clearlyInAir, bool contactGround, long nowTicks) => false;
        public bool UpdateAirToGround(bool airToContact, long nowTicks) => false;

        // ── Helpers ──────────────────────────────────────────────────────

        private float AverageDy(int fromRecent, int toRecent)
        {
            int n = toRecent - fromRecent;
            if (n <= 0 || _count < toRecent + 1) return 0f;
            float sum = 0f;
            for (int i = fromRecent; i < toRecent; i++)
                sum += GetHistory(i) - GetHistory(i + 1); // dy positif = descend
            return sum / n;
        }

        private float GetHistory(int backIndex)
        {
            int idx = (_head - 1 - backIndex + _yHistory.Length * 2) % _yHistory.Length;
            return _yHistory[idx];
        }

        private bool TryFireImpact(long nowTicks)
        {
            long cd = CooldownMs * TimeSpan.TicksPerMillisecond;
            if (_lastImpactTicks != 0 && (nowTicks - _lastImpactTicks) < cd)
                return false;
            _lastImpactTicks = nowTicks;
            return true;
        }
    }
}