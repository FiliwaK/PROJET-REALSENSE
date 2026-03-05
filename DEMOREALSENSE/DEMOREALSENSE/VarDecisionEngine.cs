using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    public enum VarEventType
    {
        None,
        GroundContact,
        AirCrossLine,
        ExitFov
    }

    public sealed class VarDecisionEngine
    {
        public int StableFrames = 3;
        public float MinAirSpeed = 120f;
        public float MaxGroundVy = 35f;
        public float MaxGroundMovePx = 6f;
        public float CrossEpsilonPx = 6f;

        public bool EnableExitFovDecision = true;
        public int ExitFovFrames = 6;

        private bool _hasLast = false;
        private PointF _lastPos;
        private long _lastTicks;

        private bool? _lastSide = null;
        private int _sideStableCount = 0;

        private int _groundStable = 0;
        private int _exitFovCount = 0;

        public bool HasDecision { get; private set; }
        public bool DecisionIsIn { get; private set; }
        public PointF DecisionPoint { get; private set; }
        public VarEventType DecisionEvent { get; private set; } = VarEventType.None;

        public void Reset()
        {
            _hasLast = false;
            _lastSide = null;
            _sideStableCount = 0;
            _groundStable = 0;
            _exitFovCount = 0;

            HasDecision = false;
            DecisionEvent = VarEventType.None;
            DecisionPoint = default;
        }

        public bool Update(
            ClickLineDetector detector, object lineLock,
            PointF pos, long ticks,
            bool inFrame,
            out VarEventType ev)
        {
            ev = VarEventType.None;
            if (HasDecision) return false;

            if (!TryGetSide(detector, lineLock, pos, out bool side, out float signedDist))
            {
                _hasLast = true;
                _lastPos = pos;
                _lastTicks = ticks;
                return false;
            }

            bool? stableSide = null;

            if (Math.Abs(signedDist) >= CrossEpsilonPx)
            {
                if (_lastSide.HasValue && _lastSide.Value == side) _sideStableCount++;
                else { _lastSide = side; _sideStableCount = 1; }

                if (_sideStableCount >= StableFrames) stableSide = _lastSide.Value;
            }

            PointF v = default;
            float speed = 0f;
            float move = 0f;

            if (_hasLast)
            {
                double dt = (ticks - _lastTicks) / (double)TimeSpan.TicksPerSecond;
                if (dt > 1e-6)
                {
                    float dx = pos.X - _lastPos.X;
                    float dy = pos.Y - _lastPos.Y;
                    v = new PointF((float)(dx / dt), (float)(dy / dt));
                    speed = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
                    move = (float)Math.Sqrt(dx * dx + dy * dy);
                }
            }

            // 1) Contact sol / roulement
            bool looksGroundNow = (Math.Abs(v.Y) <= MaxGroundVy && move <= MaxGroundMovePx);

            if (looksGroundNow) _groundStable++;
            else _groundStable = 0;

            if (_groundStable >= StableFrames)
            {
                HasDecision = true;
                DecisionIsIn = side;
                DecisionPoint = pos;
                DecisionEvent = VarEventType.GroundContact;
                ev = DecisionEvent;
                SaveLast(pos, ticks);
                return true;
            }

            // 2) Crossing en l'air
            if (stableSide.HasValue && _hasLast && speed >= MinAirSpeed)
            {
                if (TryGetSide(detector, lineLock, _lastPos, out bool prevSide, out float prevSigned) &&
                    Math.Abs(prevSigned) >= CrossEpsilonPx &&
                    prevSide != stableSide.Value)
                {
                    HasDecision = true;
                    DecisionIsIn = stableSide.Value;
                    DecisionPoint = pos;
                    DecisionEvent = VarEventType.AirCrossLine;
                    ev = DecisionEvent;
                    SaveLast(pos, ticks);
                    return true;
                }
            }

            // 3) Perte FOV (option)
            if (EnableExitFovDecision)
            {
                if (!inFrame)
                {
                    if (stableSide.HasValue && stableSide.Value == false) _exitFovCount++;
                    else _exitFovCount = 0;

                    if (_exitFovCount >= ExitFovFrames)
                    {
                        HasDecision = true;
                        DecisionIsIn = false;
                        DecisionPoint = pos;
                        DecisionEvent = VarEventType.ExitFov;
                        ev = DecisionEvent;
                        SaveLast(pos, ticks);
                        return true;
                    }
                }
                else
                {
                    _exitFovCount = 0;
                }
            }

            SaveLast(pos, ticks);
            return false;
        }

        private void SaveLast(PointF pos, long ticks)
        {
            _hasLast = true;
            _lastPos = pos;
            _lastTicks = ticks;
        }

        private static bool TryGetSide(ClickLineDetector detector, object lineLock, PointF p, out bool isIn, out float signedDist)
        {
            isIn = false;
            signedDist = 0f;

            ClickLineDetector.LineModel line;
            lock (lineLock)
            {
                if (!detector.HasLine) return false;
                line = detector.Line;
            }

            float x0 = line.Point.X;
            float y0 = line.Point.Y;
            float dx = line.Direction.X;
            float dy = line.Direction.Y;

            if (dy > 0f) { dx = -dx; dy = -dy; }

            float vx = p.X - x0;
            float vy = p.Y - y0;

            float cross = vx * dy - vy * dx;

            signedDist = cross;
            isIn = cross > 0f;
            return true;
        }
    }
}