using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    public enum VarDecisionType
    {
        None,
        In,
        Out
    }

    public enum VarDecisionReason
    {
        None,
        CrossLine,
        FirstImpact,
        Stopped
    }

    /// <summary>
    /// Décision style VAR :
    /// - OUT immédiat si la balle passe côté OUT (dépasse la ligne).
    /// - Sinon : décision au premier impact au sol (position de la croix).
    /// - Sinon : décision si la balle s'arrête.
    /// Pas de IN/OUT en continu.
    /// </summary>
    public sealed class VarDecisionEngine
    {
        // --- Réglages ---
        public int StopConfirmFrames { get; set; } = 6;   // ~6 frames stables
        public float StopMovePx { get; set; } = 2.2f;     // déplacement très faible => arrêt

        // --- Etat ---
        public bool HasDecision { get; private set; }
        public VarDecisionType Decision { get; private set; } = VarDecisionType.None;
        public VarDecisionReason Reason { get; private set; } = VarDecisionReason.None;

        public PointF? DecisionPoint { get; private set; } = null; // point de sortie/impact/arrêt
        public bool HasLine { get; private set; } = false;

        // pour détecter "passage" vers OUT
        private bool _prevSideKnown = false;
        private bool _prevIsIn = true;

        // arrêt
        private int _stopCount = 0;

        public void Reset()
        {
            HasDecision = false;
            Decision = VarDecisionType.None;
            Reason = VarDecisionReason.None;
            DecisionPoint = null;
            HasLine = false;

            _prevSideKnown = false;
            _prevIsIn = true;

            _stopCount = 0;
        }

        /// <summary>
        /// Appel frame par frame avec la position balle + infos impact/mouvement.
        /// - movePx : déplacement entre les 2 derniers points (pixels)
        /// - impactConfirmed : true si ImpactDetector confirme premier impact sol
        /// </summary>
        public void Update(
            ClickLineDetector lineDetector,
            object lineLock,
            PointF ballPos,
            float movePx,
            bool impactConfirmed)
        {
            if (HasDecision) return;

            // 1) Sans ligne => impossible de dire IN/OUT
            bool hasLine = InOutJudge.TryIsIn(lineDetector, lineLock, ballPos, out bool isInNow);
            HasLine = hasLine;

            // 2) OUT immédiat si dépasse la ligne (dès qu'on est côté OUT)
            if (hasLine)
            {
                // si on est OUT maintenant => décision immédiate OUT
                if (!isInNow)
                {
                    Commit(VarDecisionType.Out, VarDecisionReason.CrossLine, ballPos);
                    return;
                }

                // optionnel : si tu veux détecter "passage" (IN->OUT), on a déjà OUT immédiat
                if (!_prevSideKnown)
                {
                    _prevSideKnown = true;
                    _prevIsIn = isInNow;
                }
                else
                {
                    _prevIsIn = isInNow;
                }
            }

            // 3) Premier impact sol => décision basée sur position impact (croix)
            if (impactConfirmed)
            {
                // on commit IN/OUT d'après la position actuelle (point d'impact)
                if (!hasLine)
                {
                    // On marque le point mais pas de décision sans ligne
                    DecisionPoint = ballPos;
                    Reason = VarDecisionReason.FirstImpact;
                    return;
                }

                Commit(isInNow ? VarDecisionType.In : VarDecisionType.Out, VarDecisionReason.FirstImpact, ballPos);
                return;
            }

            // 4) Arrêt balle => décision IN/OUT selon position à l'arrêt
            if (movePx <= StopMovePx)
                _stopCount++;
            else
                _stopCount = 0;

            if (_stopCount >= StopConfirmFrames)
            {
                if (!hasLine)
                {
                    DecisionPoint = ballPos;
                    Reason = VarDecisionReason.Stopped;
                    return;
                }

                Commit(isInNow ? VarDecisionType.In : VarDecisionType.Out, VarDecisionReason.Stopped, ballPos);
                return;
            }
        }

        private void Commit(VarDecisionType decision, VarDecisionReason reason, PointF p)
        {
            HasDecision = true;
            Decision = decision;
            Reason = reason;
            DecisionPoint = p;
        }
    }
}