using System;

namespace DEMOREALSENSE
{
    public sealed class InOutLatch
    {
        public int OutHoldMs { get; set; } = 5000; // ✅ 5 secondes

        public bool HasState { get; private set; }
        public bool CurrentIsIn { get; private set; } = true;

        public bool IsLatchedOut { get; private set; }
        private long _latchedUntilTicks = 0;

        public void Reset()
        {
            HasState = false;
            CurrentIsIn = true;
            IsLatchedOut = false;
            _latchedUntilTicks = 0;
        }

        public void Update(bool isInNow, long nowTicks)
        {
            // Si OUT locké => on ignore tout jusqu'à expiration
            if (IsLatchedOut)
            {
                if (nowTicks >= _latchedUntilTicks)
                {
                    // fin du lock => reset puis on reprend avec l’état courant
                    Reset();
                    HasState = true;
                    CurrentIsIn = isInNow;
                }
                return;
            }

            HasState = true;
            CurrentIsIn = isInNow;

            if (!isInNow)
            {
                IsLatchedOut = true;
                _latchedUntilTicks = nowTicks + TimeSpan.FromMilliseconds(OutHoldMs).Ticks;
            }
        }

        public int LatchedRemainingMs(long nowTicks)
        {
            if (!IsLatchedOut) return 0;
            long rem = _latchedUntilTicks - nowTicks;
            if (rem <= 0) return 0;
            return (int)Math.Ceiling(rem / (double)TimeSpan.TicksPerMillisecond);
        }
    }
}