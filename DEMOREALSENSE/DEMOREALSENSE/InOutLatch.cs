using System;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Logique IN/OUT "collée":
    /// - IN s'affiche en temps réel tant qu'on n'a pas latch OUT.
    /// - Dès qu'on passe OUT, on "lock" OUT pour OutHoldMs.
    /// - Pendant le lock, même si on repasse IN, on NE REDEMARRE PAS le timer
    ///   et on NE change PAS l'état affiché.
    /// - Quand le délai expire, on réinitialise et on repart normal.
    /// </summary>
    public sealed class InOutLatch
    {
        public int OutHoldMs { get; set; } = 5000; // ✅ 5 secondes

        // Etat courant (quand pas locké)
        public bool HasState { get; private set; } = false;
        public bool CurrentIsIn { get; private set; } = true;

        // Lock OUT
        public bool IsLatchedOut { get; private set; } = false;

        private long _latchedUntilTicks = 0;

        public void Reset()
        {
            HasState = false;
            CurrentIsIn = true;
            IsLatchedOut = false;
            _latchedUntilTicks = 0;
        }

        /// <summary>
        /// Appeler à chaque frame quand tu as un isInNow.
        /// nowTicks = DateTime.UtcNow.Ticks
        /// </summary>
        public void Update(bool isInNow, long nowTicks)
        {
            // 1) Si OUT locké, on ignore tout jusqu'à expiration
            if (IsLatchedOut)
            {
                if (nowTicks >= _latchedUntilTicks)
                {
                    // ✅ lock terminé => reset complet et on repart normal
                    Reset();

                    // option: on enregistre tout de suite l'état actuel après reset
                    HasState = true;
                    CurrentIsIn = isInNow;
                }
                return;
            }

            // 2) Pas locké => comportement normal
            HasState = true;
            CurrentIsIn = isInNow;

            // 3) Si on devient OUT => on lock 5s
            if (!isInNow)
            {
                IsLatchedOut = true;
                _latchedUntilTicks = nowTicks + TimeSpan.FromMilliseconds(OutHoldMs).Ticks;
            }
        }

        /// <summary>
        /// Temps restant (ms) avant délock OUT. Retourne 0 si pas locké.
        /// </summary>
        public int LatchedRemainingMs(long nowTicks)
        {
            if (!IsLatchedOut) return 0;

            long remTicks = _latchedUntilTicks - nowTicks;
            if (remTicks <= 0) return 0;

            double ms = remTicks * 1000.0 / TimeSpan.TicksPerSecond;
            return (int)Math.Ceiling(ms);
        }
    }
}