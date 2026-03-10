using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class FrameResult
    {
        public bool HasFrame { get; set; }

        // Image déjà prête pour UI (ownership: UI doit Dispose l’ancienne)
        public Bitmap? BitmapToShow { get; set; }

        // Manual tracker (si actif)
        public bool ManualTrackingOk { get; set; } = true;

        // Infos distance
        public ushort RawDepth { get; set; }
        public float DepthUnits { get; set; }

        // temps frame (ms)
        public double FrameMs { get; set; }

        // horloge
        public long NowTicks { get; set; }

        // latch IN/OUT
        public InOutLatch Latch { get; set; } = new InOutLatch();
    }
}