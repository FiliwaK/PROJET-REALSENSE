using System;
using System.Drawing;
using System.Windows.Forms;

namespace DEMOREALSENSE
{
    public sealed class HudPresenter
    {
        private readonly Label _distanceLabel;
        private readonly Label _frameLabel;

        private long _lastUiTicks = 0;
        private long _uiMinTicks = TimeSpan.TicksPerSecond / 10;

        // message temporaire
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

        public void RenderHelpOrDistance(
            long nowTicks,
            string helpText,
            bool showDistance,
            ushort rawDepth,
            float depthUnits,
            InOutLatch latch)
        {
            if (nowTicks - _lastUiTicks < _uiMinTicks) return;
            _lastUiTicks = nowTicks;

            // priorité: message temporaire
            if (_msgText != null && nowTicks <= _msgUntilTicks)
            {
                _distanceLabel.ForeColor = _msgColor;
                _distanceLabel.Text = _msgText;
                return;
            }
            _msgText = null;

            if (!showDistance)
            {
                _distanceLabel.ForeColor = Color.Black;
                _distanceLabel.Text = helpText;
                return;
            }

            // show distance + in/out
            string distText = "--";
            if (rawDepth != 0)
            {
                var (m, cm) = DistanceCalculator.RawToMetersCm(rawDepth, depthUnits);
                distText = $"{m:0.000} m ({cm:0.0} cm)";
            }

            // IN/OUT
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
            _frameLabel.Text = $"Traitement moyen: {frameMs:0.0} ms/frame";
        }
    }
}