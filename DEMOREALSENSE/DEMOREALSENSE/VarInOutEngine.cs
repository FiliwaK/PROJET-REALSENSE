using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    public enum InOutSide { Unknown, In, Out }
    public enum VerdictState { None, Tracking, PendingImpact, Final }

    public sealed class VarInOutEngine
    {
        // --- Stabilité ---
        public int ConfirmFrames { get; set; } = 4;

        // Si true: OUT confirmé en l'air => verdict immédiat OUT
        public bool FinalizeOnAirOut { get; set; } = true;

        // Evite double impact
        public int ImpactCooldownMs { get; set; } = 700;

        // --- Etat ---
        public VerdictState State { get; private set; } = VerdictState.None;
        public InOutSide StableSide { get; private set; } = InOutSide.Unknown;
        public InOutSide FinalVerdict { get; private set; } = InOutSide.Unknown;

        public InOutSide RawSide { get; private set; } = InOutSide.Unknown;
        public int StableCount { get; private set; } = 0;

        // --- Impact visuel ---
        public PointF? GroundContactPoint { get; private set; } = null;
        public long GroundContactTicks { get; private set; } = 0;

        // --- Debug utile ---
        public PointF LastBall { get; private set; }

        private InOutSide _candidate = InOutSide.Unknown;
        private int _candidateCount = 0;
        private long _lastImpactTicks = 0;

        public void Reset()
        {
            State = VerdictState.None;
            StableSide = InOutSide.Unknown;
            FinalVerdict = InOutSide.Unknown;

            RawSide = InOutSide.Unknown;
            StableCount = 0;

            _candidate = InOutSide.Unknown;
            _candidateCount = 0;

            GroundContactPoint = null;
            GroundContactTicks = 0;
            _lastImpactTicks = 0;
        }

        /// <summary>
        /// Update à chaque frame où on a une position de balle.
        /// sideNow: IN/OUT brut (cross product)
        /// impactNow: true si on détecte le 1er contact "sol" (ou début roulage)
        /// </summary>
        public void Update(InOutSide sideNow, PointF ballPos, bool impactNow)
        {
            LastBall = ballPos;
            RawSide = sideNow;

            if (State == VerdictState.Final) return;
            if (State == VerdictState.None) State = VerdictState.Tracking;

            // 1) Stabilisation IN/OUT (évite bruit)
            if (sideNow != InOutSide.Unknown)
            {
                if (sideNow != _candidate)
                {
                    _candidate = sideNow;
                    _candidateCount = 1;
                }
                else
                {
                    _candidateCount++;
                }

                if (_candidateCount >= ConfirmFrames)
                {
                    StableSide = _candidate;
                    StableCount = _candidateCount;

                    if (FinalizeOnAirOut && StableSide == InOutSide.Out)
                    {
                        FinalVerdict = InOutSide.Out;
                        State = VerdictState.Final;
                        return;
                    }

                    State = VerdictState.PendingImpact;
                }
            }

            // 2) Impact -> finalisation (si pas déjà final)
            if (impactNow)
            {
                long now = DateTime.UtcNow.Ticks;
                if (now - _lastImpactTicks < TimeSpan.FromMilliseconds(ImpactCooldownMs).Ticks)
                    return;

                _lastImpactTicks = now;

                // point de contact sol (pour croix + label)
                GroundContactPoint = ballPos;
                GroundContactTicks = now;

                // Si OUT stable déjà -> OUT final
                if (StableSide == InOutSide.Out)
                {
                    FinalVerdict = InOutSide.Out;
                    State = VerdictState.Final;
                    return;
                }

                // Sinon verdict = côté au moment de l'impact (ou stable si unknown)
                if (sideNow != InOutSide.Unknown)
                    FinalVerdict = sideNow;
                else if (StableSide != InOutSide.Unknown)
                    FinalVerdict = StableSide;
                else
                    FinalVerdict = InOutSide.Unknown;

                State = VerdictState.Final;
            }
        }
    }
}