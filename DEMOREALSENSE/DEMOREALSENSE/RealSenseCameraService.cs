using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Intel.RealSense;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Service caméra RealSense : démarre/stop + récupère frame Color (RGB8) + Depth (Z16) alignée sur la couleur.
    /// Pas d'UI ici.
    /// </summary>
    public sealed class RealSenseCameraService : IDisposable
    {
        private Pipeline? _pipe;
        private Align? _alignToColor;

        public bool IsRunning { get; private set; }

        public float DepthUnits { get; private set; } = 0.001f;

        public int ColorW { get; private set; }
        public int ColorH { get; private set; }

        public int DepthW { get; private set; }
        public int DepthH { get; private set; }

        public void Start(int w = 640, int h = 480, int fps = 30)
        {
            Stop();

            using (var ctx = new Context())
            {
                var devs = ctx.QueryDevices();
                if (devs.Count == 0)
                    throw new InvalidOperationException("Aucune RealSense détectée (USB / driver).");
            }

            _pipe = new Pipeline();
            _alignToColor = new Align(Intel.RealSense.Stream.Color);

            var cfg = new Config();
            cfg.EnableStream(Intel.RealSense.Stream.Color, w, h, Format.Rgb8, fps);
            cfg.EnableStream(Intel.RealSense.Stream.Depth, w, h, Format.Z16, fps);

            var profile = _pipe.Start(cfg);

            // DepthUnits si dispo
            try
            {
                foreach (var s in profile.Device.Sensors)
                {
                    try
                    {
                        DepthUnits = s.Options[Option.DepthUnits].Value;
                        Debug.WriteLine($"DepthUnits = {DepthUnits}");
                        break;
                    }
                    catch { }
                }
            }
            catch { }

            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;

            _alignToColor?.Dispose();
            _alignToColor = null;

            try { _pipe?.Stop(); } catch { }
            _pipe?.Dispose();
            _pipe = null;
        }

        /// <summary>
        /// Lit une frame alignée : rgb = R,G,B... (w*h*3) ; depthU16 = Z16 (w*h)
        /// </summary>
        public bool TryGetAlignedFrames(uint timeoutMs, out byte[] rgb, out ushort[] depthU16)
        {
            rgb = Array.Empty<byte>();
            depthU16 = Array.Empty<ushort>();

            if (!IsRunning || _pipe == null || _alignToColor == null)
                return false;

            using var frames = _pipe.WaitForFrames(timeoutMs);

            using var alignedFrame = _alignToColor.Process(frames);
            using var aligned = alignedFrame.As<FrameSet>();

            using var color = aligned.ColorFrame;
            using var depth = aligned.DepthFrame;

            if (color == null || depth == null)
                return false;

            ColorW = color.Width;
            ColorH = color.Height;

            DepthW = depth.Width;
            DepthH = depth.Height;

            // RGB8
            rgb = new byte[ColorW * ColorH * 3];
            Marshal.Copy(color.Data, rgb, 0, rgb.Length);

            // Z16 -> ushort[]
            int bytes = DepthW * DepthH * 2;
            byte[] tmp = new byte[bytes];
            Marshal.Copy(depth.Data, tmp, 0, bytes);

            depthU16 = new ushort[DepthW * DepthH];
            Buffer.BlockCopy(tmp, 0, depthU16, 0, bytes);

            return true;
        }

        public void Dispose() => Stop();
    }
}