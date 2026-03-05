using System;
using System.Collections.Generic;
using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class TrajectoryTracker
    {
        public struct Sample
        {
            public long Ticks;
            public PointF Pos;
        }

        private readonly Queue<Sample> _samples = new Queue<Sample>();

        public int MaxSamples { get; set; } = 30;

        public void Reset() => _samples.Clear();

        public void Add(PointF p, long ticks)
        {
            _samples.Enqueue(new Sample { Pos = p, Ticks = ticks });
            while (_samples.Count > MaxSamples) _samples.Dequeue();
        }

        public bool TryGetVelocity(out PointF v)
        {
            v = default;
            if (_samples.Count < 3) return false;

            var arr = _samples.ToArray();
            var a = arr[arr.Length - 3];
            var b = arr[arr.Length - 1];

            double dt = (b.Ticks - a.Ticks) / (double)TimeSpan.TicksPerSecond;
            if (dt <= 1e-6) return false;

            v = new PointF(
                (float)((b.Pos.X - a.Pos.X) / dt),
                (float)((b.Pos.Y - a.Pos.Y) / dt)
            );
            return true;
        }

        public bool TryGetPreviousVelocity(out PointF vPrev)
        {
            vPrev = default;
            if (_samples.Count < 5) return false;

            var arr = _samples.ToArray();
            var a = arr[arr.Length - 5];
            var b = arr[arr.Length - 3];

            double dt = (b.Ticks - a.Ticks) / (double)TimeSpan.TicksPerSecond;
            if (dt <= 1e-6) return false;

            vPrev = new PointF(
                (float)((b.Pos.X - a.Pos.X) / dt),
                (float)((b.Pos.Y - a.Pos.Y) / dt)
            );
            return true;
        }

        public bool TryGetLastTwoPositions(out PointF prev, out PointF last)
        {
            prev = last = default;
            if (_samples.Count < 2) return false;

            var arr = _samples.ToArray();
            prev = arr[arr.Length - 2].Pos;
            last = arr[arr.Length - 1].Pos;
            return true;
        }
    }
}