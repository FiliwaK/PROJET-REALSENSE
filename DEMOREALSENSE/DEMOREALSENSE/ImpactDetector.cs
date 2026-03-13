using System;

namespace DEMOREALSENSE
{
    public sealed class ImpactDetector
    {
        public int CooldownMs { get; set; } = 450;

        private long _lastImpactTicks = 0;

        public void Reset() => _lastImpactTicks = 0;

        /// <summary>
        /// AIR->CONTACT uniquement (sinon PAS d'impact).
        /// </summary>
        public bool UpdateAirToGround(bool airToContact, long nowTicks)
        {
            if (!airToContact) return false;

            long cd = TimeSpan.FromMilliseconds(CooldownMs).Ticks;
            if (_lastImpactTicks != 0 && (nowTicks - _lastImpactTicks) < cd)
                return false;

            _lastImpactTicks = nowTicks;
            return true;
        }
    }
}