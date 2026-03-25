using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class FrameResult
    {
        public bool HasFrame { get; set; }

        public Bitmap? BitmapToShow { get; set; }

        public bool ManualTrackingOk { get; set; } = true;

        public ushort RawDepth { get; set; }
        public float DepthUnits { get; set; }

        public double FrameMs { get; set; }
        public long NowTicks { get; set; }

        // Latch legacy
        public InOutLatch Latch { get; set; } = new InOutLatch();

        // VarEngine conservé pour compat
        public VarInOutEngine? VarEngine { get; set; }

        // Verdict live direct (mis à jour chaque frame)
        public InOutSide LiveSide { get; set; } = InOutSide.Unknown;
        public bool VerdictHeld { get; set; } = false;
        public long VerdictHeldTicks { get; set; } = 0;

        // ✅ État du plan de table (pour affichage dans le HUD)
        public bool TablePlaneReady { get; set; } = false;
    }
}