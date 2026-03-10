using System;

namespace DEMOREALSENSE
{
    public sealed class ImpactDetector
    {
        // anti doublons
        public int CooldownMs { get; set; } = 500;

        private bool _wasInContact = false;
        private long _lastImpactTicks = 0;

        public void Reset()
        {
            _wasInContact = false;
            _lastImpactTicks = 0;
        }

        /// <summary>
        /// Update avec un bool "contactSol".
        /// Retourne true UNIQUEMENT au moment où on passe de "pas contact" -> "contact".
        /// </summary>
        public bool Update(bool contactSol, long nowTicks)
        {
            bool risingEdge = (!_wasInContact && contactSol);
            _wasInContact = contactSol;

            if (!risingEdge) return false;

            long cd = TimeSpan.FromMilliseconds(CooldownMs).Ticks;
            if (_lastImpactTicks != 0 && (nowTicks - _lastImpactTicks) < cd)
                return false;

            _lastImpactTicks = nowTicks;
            return true;
        }
    }
}