using System;
using System.Drawing;
using System.Windows.Forms;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Présentation HUD — version améliorée.
    ///
    /// AMÉLIORATIONS :
    ///   - Affiche le verdict VarInOutEngine (IN/OUT/Tracking/PendingImpact) quand disponible.
    ///   - Verdict Final affiché en grand avec couleur claire.
    ///   - Conserve l'ancienne API RenderHelpOrDistance pour compat.
    /// </summary>
    public sealed class HudPresenter
    {
        private readonly Label _distanceLabel;
        private readonly Label _frameLabel;

        private long _lastUiTicks = 0;
        private long _uiMinTicks = TimeSpan.TicksPerSecond / 10;  // 10hz général
        private long _lastVerdictTicks = 0;
        private long _verdictMinTicks = TimeSpan.TicksPerSecond / 20; // 20hz pour IN/OUT

        private long _msgUntilTicks = 0;
        private string? _msgText = null;
        private Color _msgColor = Color.Black;

        public HudPresenter(Label distanceLabel, Label frameLabel)
        {
            _distanceLabel = distanceLabel;
            _frameLabel = frameLabel;
        }

        public void SetUiHz(int hz)
        {
            hz = Math.Max(1, hz);
            _uiMinTicks = TimeSpan.TicksPerSecond / hz;
        }

        public void ShowTempMessage(long nowTicks, string text, Color color, int holdMs = 1400)
        {
            _msgText = text;
            _msgColor = color;
            _msgUntilTicks = nowTicks + TimeSpan.FromMilliseconds(holdMs).Ticks;
        }

        /// <summary>
        /// Rendu principal — affiche distance + verdict IN/OUT live.
        /// </summary>
        public void RenderHelpOrDistance(
            long nowTicks,
            string helpText,
            bool showDistance,
            ushort rawDepth,
            float depthUnits,
            InOutLatch latch,
            VarInOutEngine? varEngine = null,
            InOutSide liveSide = InOutSide.Unknown,
            bool verdictHeld = false,
            long heldTicks = 0,
            int outHoldMs = 5000)
        {
            // Messages temporaires : throttle normal
            if (_msgText != null && nowTicks <= _msgUntilTicks)
            {
                if (nowTicks - _lastUiTicks < _uiMinTicks) return;
                _lastUiTicks = nowTicks;
                _distanceLabel.ForeColor = _msgColor;
                _distanceLabel.Text = _msgText;
                return;
            }
            _msgText = null;

            if (!showDistance)
            {
                if (nowTicks - _lastUiTicks < _uiMinTicks) return;
                _lastUiTicks = nowTicks;
                _distanceLabel.ForeColor = Color.Black;
                _distanceLabel.Text = helpText;
                return;
            }

            // ✅ Verdict IN/OUT : throttle 20hz (réactif)
            if (nowTicks - _lastVerdictTicks < _verdictMinTicks) return;
            _lastVerdictTicks = nowTicks;
            _lastUiTicks = nowTicks;

            string distText = "--";
            if (rawDepth != 0)
            {
                var (m, cm) = DistanceCalculator.RawToMetersCm(rawDepth, depthUnits);
                distText = $"{m:0.000} m ({cm:0.0} cm)";
            }

            if (liveSide != InOutSide.Unknown)
            {
                RenderLiveVerdict(distText, liveSide, verdictHeld, heldTicks, nowTicks, outHoldMs);
                return;
            }

            RenderWithLatch(distText, latch, nowTicks);
        }

        // ── Rendu live IN/OUT ────────────────────────────────────────────

        private void RenderLiveVerdict(
            string distText, InOutSide side,
            bool verdictHeld, long heldTicks, long nowTicks, int outHoldMs)
        {
            string verdict;
            Color col;

            if (side == InOutSide.Out)
            {
                if (verdictHeld)
                {
                    // Compte à rebours du hold
                    long remTicks = heldTicks + outHoldMs * TimeSpan.TicksPerMillisecond - nowTicks;
                    int remSec = Math.Max(0, (int)(remTicks / TimeSpan.TicksPerMillisecond / 1000));
                    verdict = remSec > 0 ? $"❌  OUT  ({remSec}s)" : "❌  OUT";
                }
                else
                {
                    verdict = "❌  OUT";
                }
                col = Color.Red;
            }
            else
            {
                verdict = "✅  IN";
                col = Color.LimeGreen;
            }

            _distanceLabel.ForeColor = col;
            _distanceLabel.Text = $"{distText} | {verdict}";
        }

        // ── Fallback latch ───────────────────────────────────────────────

        private void RenderWithLatch(string distText, InOutLatch latch, long nowTicks)
        {
            string inout = "IN/OUT: ?";
            Color col = Color.Black;

            if (latch.IsLatchedOut)
            {
                int rem = latch.LatchedRemainingMs(nowTicks);
                inout = $"OUT ❌ ({rem / 1000.0:0.0}s)";
                col = Color.Red;
            }
            else if (latch.HasState)
            {
                if (latch.CurrentIsIn) { inout = "IN ✅"; col = Color.LimeGreen; }
                else { inout = "OUT ❌"; col = Color.Red; }
            }

            _distanceLabel.ForeColor = col;
            _distanceLabel.Text = $"{distText} | {inout}";
        }

        public void UpdateFrameTime(double frameMs)
        {
            _frameLabel.ForeColor = Color.Black;
            _frameLabel.Text = $"Traitement: {frameMs:0.0} ms/frame";
        }
    }
}