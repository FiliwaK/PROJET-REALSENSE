namespace DEMOREALSENSE
{
    public sealed class InOutLatch
    {
        public int OutHoldMs { get; set; } = 10_000;

        public bool HasState { get; private set; }
        public bool CurrentIsIn { get; private set; }

        private long _outUntilTicks = 0;

        public bool IsLatchedOut => _outUntilTicks != 0 && System.DateTime.UtcNow.Ticks < _outUntilTicks;

        public void Reset()
        {
            HasState = false;
            CurrentIsIn = true;
            _outUntilTicks = 0;
        }

        public void Update(bool isInNow, long nowTicks)
        {
            HasState = true;
            CurrentIsIn = isInNow;

            if (!isInNow)
                _outUntilTicks = nowTicks + System.TimeSpan.FromMilliseconds(OutHoldMs).Ticks;
        }

        public int LatchedRemainingMs(long nowTicks)
        {
            if (_outUntilTicks <= nowTicks) return 0;
            return (int)System.TimeSpan.FromTicks(_outUntilTicks - nowTicks).TotalMilliseconds;
        }
    }
}