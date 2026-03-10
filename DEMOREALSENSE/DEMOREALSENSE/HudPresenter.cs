using System;
using System.Drawing;
using System.Windows.Forms;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Gère l'affichage des labels (distance + IN/OUT latch + perf ms/frame),
    /// avec throttle et EMA.
    /// </summary>
    public sealed class HudPresenter
    {
        private readonly Label _distanceLabel;
        private readonly Label _frameLabel;

        private long _lastUiTicks = 0;
        private long _uiMinTicks = TimeSpan.TicksPerSecond / 10;

        private double _emaMs = 0.0;
        private const double EmaAlpha = 0.08;

        public HudPresenter(Label distanceLabel, Label traitementFrameLabel)
        {
            _distanceLabel = distanceLabel;
            _frameLabel = traitementFrameLabel;
        }

        public void SetUiHz(int hz)
        {
            hz = Math.Max(1, hz);
            _uiMinTicks = TimeSpan.TicksPerSecond / hz;
        }

        public void UpdateFrameTime(double ms)
        {
            if (_emaMs <= 0.0001) _emaMs = ms;
            else _emaMs = _emaMs + EmaAlpha * (ms - _emaMs);

            _frameLabel.ForeColor = Color.Black;
            _frameLabel.Text = $"Traitement moyen: {_emaMs:0.0} ms/frame";
        }

        public void UpdateDistanceAndInOut(
            long nowTicks,
            ushort rawDepth,
            float depthUnits,
            string prefix,
            InOutLatch latch)
        {
            if (nowTicks - _lastUiTicks < _uiMinTicks) return;
            _lastUiTicks = nowTicks;

            string distText = $"{prefix}: --";
            if (rawDepth != 0)
            {
                var (m, cm) = DistanceCalculator.RawToMetersCm(rawDepth, depthUnits);
                distText = $"{prefix}: {m:0.000} m ({cm:0.0} cm)";
            }

            string inout = " | IN/OUT: ? (trace ligne)";
            Color col = Color.Black;

            if (latch.IsLatchedOut)
            {
                int rem = latch.LatchedRemainingMs(nowTicks);
                inout = $" | OUT ❌ (hold {rem / 1000.0:0.0}s)";
                col = Color.Red;
            }
            else if (latch.HasState)
            {
                if (latch.CurrentIsIn)
                {
                    inout = " | IN ✅";
                    col = Color.LimeGreen;
                }
                else
                {
                    inout = " | OUT ❌";
                    col = Color.Red;
                }
            }

            _distanceLabel.ForeColor = col;
            _distanceLabel.Text = distText + inout;
        }

        public void ShowMessage(long nowTicks, string text, Color color)
        {
            if (nowTicks - _lastUiTicks < _uiMinTicks) return;
            _lastUiTicks = nowTicks;

            _distanceLabel.ForeColor = color;
            _distanceLabel.Text = text;
        }

        public void SetStatus(string text)
        {
            _distanceLabel.ForeColor = Color.Black;
            _distanceLabel.Text = text;
        }
    }
}