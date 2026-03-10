using System.Drawing;

namespace DEMOREALSENSE
{
    public sealed class FrameResult
    {
        public bool HasFrame { get; set; }
        public Bitmap? BitmapToShow { get; set; }

        public long NowTicks { get; set; }
        public double FrameMs { get; set; }

        // qui est suivi ?
        public bool HasBall { get; set; }
        public int BallX { get; set; }
        public int BallY { get; set; }

        // profondeur de la balle (raw) + units (m)
        public ushort RawDepth { get; set; }
        public float DepthUnits { get; set; }

        // tracking manuel
        public bool ManualTrackingOk { get; set; }

        // état IN/OUT (latch)
        public InOutLatch Latch { get; set; } = new InOutLatch();

        // impact (croix)
        public bool HasImpact { get; set; }
        public PointF ImpactPoint { get; set; }

        // debug sol
        public bool HasGround { get; set; }
        public float GroundY { get; set; }

        // source affichage HUD
        public string Source { get; set; } = "AUTO"; // "AUTO" / "MANUAL" / "NONE"
    }
}