using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Intel.RealSense;

namespace DEMOREALSENSE
{
    public partial class CameraView : Form
    {
        private Pipeline? _pipe;
        private CancellationTokenSource? _cts;
        private Task? _task;

        // Align depth -> color
        private Align? _alignToColor;

        // Dernière depth alignée (Z16) en RAM
        private volatile ushort[]? _lastDepthU16;
        private volatile int _depthW, _depthH;
        private volatile float _depthUnits = 0.001f;

        // Dernière couleur RGB8 en RAM (pour tracking)
        private readonly object _colorLock = new();
        private byte[]? _lastColorRgb;
        private int _colorW, _colorH;

        // ===== Tracking template =====
        private volatile bool _tracking = false;
        private volatile int _trackX = -1, _trackY = -1; // position suivie (pixel image)
        private byte[]? _templateGray;                   // patch en grayscale
        private int _tplSize = 31;                       // patch NxN (impair)
        private int _searchRadius = 40;                  // fenêtre de recherche autour du point

        // ===== Overlay carré vert =====
        private const int BOX_HALF = 12;                 // carré 24x24
        private const int DIST_RADIUS = 2;               // médiane depth sur 5x5

        // UI throttle (10 Hz pour le label)
        private long _lastUiTicks = 0;
        private static readonly long UiMinTicks = TimeSpan.TicksPerSecond / 10;

        public CameraView()
        {
            InitializeComponent();

            cameraPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            cameraPictureBox.BackColor = Color.Black;
            cameraPictureBox.BringToFront();
            distanceLabel.BringToFront();

            cameraPictureBox.MouseClick += CameraPictureBox_MouseClick;

            distanceLabel.Text = "Caméra prête. Clique sur un objet pour le suivre.";
            Log("UI Ready");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartColorDepth();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopAll();
            base.OnFormClosing(e);
        }

        private void StartColorDepth()
        {
            try
            {
                StopAll();

                SetStatus("Init RealSense...");

                using (var ctx = new Context())
                {
                    var devs = ctx.QueryDevices();
                    Log($"Devices detected: {devs.Count}");
                    if (devs.Count == 0)
                    {
                        SetStatus("Aucune RealSense détectée (USB / driver).");
                        return;
                    }
                }

                _cts = new CancellationTokenSource();
                _pipe = new Pipeline();

                var cfg = new Config();
                cfg.EnableStream(Intel.RealSense.Stream.Color, 640, 480, Format.Rgb8, 30);
                cfg.EnableStream(Intel.RealSense.Stream.Depth, 640, 480, Format.Z16, 30);

                _alignToColor = new Align(Intel.RealSense.Stream.Color);

                SetStatus("Start pipeline...");
                var profile = _pipe.Start(cfg);

                // DepthUnits si dispo
                try
                {
                    foreach (var s in profile.Device.Sensors)
                    {
                        try
                        {
                            _depthUnits = s.Options[Option.DepthUnits].Value;
                            Log($"DepthUnits = {_depthUnits}");
                            break;
                        }
                        catch { }
                    }
                }
                catch { }

                SetStatus("OK. Clique pour sélectionner l'objet à suivre.");
                _task = Task.Run(() => Loop(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                SetStatus("Start error: " + ex.Message);
                Log(ex.ToString());
            }
        }

        private void StopAll()
        {
            try
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                    try { _task?.Wait(500); } catch { }
                }
                _task = null;

                _alignToColor?.Dispose();
                _alignToColor = null;

                try { _pipe?.Stop(); } catch { }
                _pipe?.Dispose();
                _pipe = null;

                _cts?.Dispose();
                _cts = null;

                _lastDepthU16 = null;
                _depthW = _depthH = 0;

                lock (_colorLock)
                {
                    _lastColorRgb = null;
                    _colorW = _colorH = 0;
                }

                _tracking = false;
                _trackX = _trackY = -1;
                _templateGray = null;

                SafeUI(() =>
                {
                    var old = cameraPictureBox.Image;
                    cameraPictureBox.Image = null;
                    old?.Dispose();
                    distanceLabel.Text = "Arrêté.";
                });
            }
            catch { }
        }

        private void Loop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Bitmap? toShow = null;

                try
                {
                    using var frames = _pipe!.WaitForFrames(2000);

                    var align = _alignToColor;
                    if (align == null) continue;

                    using var alignedFrame = align.Process(frames);
                    using var aligned = alignedFrame.As<FrameSet>();

                    using var color = aligned.ColorFrame;
                    using var depth = aligned.DepthFrame;

                    if (color == null || depth == null) continue;

                    int w = color.Width;
                    int h = color.Height;

                    // ===== 1) Copie COLOR RGB8 en RAM pour tracking =====
                    byte[] rgb = new byte[w * h * 3];
                    Marshal.Copy(color.Data, rgb, 0, rgb.Length);

                    lock (_colorLock)
                    {
                        _lastColorRgb = rgb;
                        _colorW = w;
                        _colorH = h;
                    }

                    // ===== 2) Copie DEPTH Z16 en RAM =====
                    int dw = depth.Width;
                    int dh = depth.Height;
                    int bytes = dw * dh * 2;
                    byte[] tmp = new byte[bytes];
                    Marshal.Copy(depth.Data, tmp, 0, bytes);
                    ushort[] depthU16 = new ushort[dw * dh];
                    Buffer.BlockCopy(tmp, 0, depthU16, 0, bytes);

                    _lastDepthU16 = depthU16;
                    _depthW = dw;
                    _depthH = dh;

                    // ===== 3) Tracking: template matching autour du dernier point =====
                    if (_tracking && _templateGray != null && _trackX >= 0 && _trackY >= 0)
                    {
                        // cherche la meilleure position proche
                        if (TryTemplateTrack(rgb, w, h, _templateGray, _tplSize, _trackX, _trackY, _searchRadius,
                            out int nx, out int ny, out int bestScore))
                        {
                            _trackX = nx;
                            _trackY = ny;

                            // distance auto (médiane 5x5)
                            ushort rawMed = MedianDepthRaw(depthU16, dw, dh, nx, ny, DIST_RADIUS);
                            if (rawMed > 0)
                            {
                                float meters = rawMed * _depthUnits;
                                float cm = meters * 100f;

                                long now = DateTime.UtcNow.Ticks;
                                if (now - _lastUiTicks >= UiMinTicks)
                                {
                                    _lastUiTicks = now;
                                    SafeUI(() => distanceLabel.Text = $"{meters:0.000} m  ({cm:0.0} cm)");
                                }
                            }
                            else
                            {
                                long now = DateTime.UtcNow.Ticks;
                                if (now - _lastUiTicks >= UiMinTicks)
                                {
                                    _lastUiTicks = now;
                                    SafeUI(() => distanceLabel.Text = "Objet suivi, mais profondeur invalide.");
                                }
                            }
                        }
                        else
                        {
                            long now = DateTime.UtcNow.Ticks;
                            if (now - _lastUiTicks >= UiMinTicks)
                            {
                                _lastUiTicks = now;
                                SafeUI(() => distanceLabel.Text = "Objet perdu (reclique).");
                            }
                        }
                    }

                    // ===== 4) Affichage: on reconstruit Bitmap depuis rgb (évite 2 copies) =====
                    toShow = RgbToBitmap24bpp(rgb, w, h);

                    // carré vert sur la position suivie
                    if (_tracking && _trackX >= 0 && _trackY >= 0)
                    {
                        DrawGreenBox(toShow, _trackX, _trackY);
                    }

                    Bitmap final = toShow;
                    toShow = null;

                    SafeUI(() =>
                    {
                        var old = cameraPictureBox.Image;
                        cameraPictureBox.Image = final;
                        old?.Dispose();
                    });
                }
                catch (Exception ex)
                {
                    toShow?.Dispose();
                    if (token.IsCancellationRequested) break;
                    Log("Loop error: " + ex);
                    Thread.Sleep(30);
                }
            }
        }

        private void CameraPictureBox_MouseClick(object? sender, MouseEventArgs e)
        {
            byte[]? rgb;
            int w, h;

            lock (_colorLock)
            {
                rgb = _lastColorRgb;
                w = _colorW;
                h = _colorH;
            }

            if (rgb == null || w <= 0 || h <= 0 || cameraPictureBox.Image == null)
            {
                distanceLabel.Text = "Image pas prête (attends 1-2 secondes).";
                return;
            }

            var (x, y) = TranslateZoomMousePositionToImagePixel(cameraPictureBox, e.Location);
            if (x < 0 || y < 0 || x >= w || y >= h)
            {
                distanceLabel.Text = "Clique dans l'image (pas dans les bandes).";
                return;
            }

            // créer template autour du point cliqué
            if (!TryCreateTemplate(rgb, w, h, x, y, _tplSize, out var tpl))
            {
                distanceLabel.Text = "Impossible de créer le template (bord image).";
                return;
            }

            _templateGray = tpl;
            _trackX = x;
            _trackY = y;
            _tracking = true;

            distanceLabel.Text = "Tracking activé (distance auto).";
            Log($"Tracking ON at ({x},{y}), tpl={_tplSize} search={_searchRadius}");
        }

        // ===== Template creation (grayscale patch) =====
        private static bool TryCreateTemplate(byte[] rgb, int w, int h, int cx, int cy, int size, out byte[] tplGray)
        {
            tplGray = Array.Empty<byte>();
            if (size < 9) size = 9;
            if ((size & 1) == 0) size++; // impair

            int half = size / 2;
            int x0 = cx - half;
            int y0 = cy - half;
            int x1 = cx + half;
            int y1 = cy + half;

            if (x0 < 0 || y0 < 0 || x1 >= w || y1 >= h) return false;

            tplGray = new byte[size * size];
            int ti = 0;

            for (int y = y0; y <= y1; y++)
            {
                int row = y * w * 3;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x * 3;
                    byte r = rgb[i + 0];
                    byte g = rgb[i + 1];
                    byte b = rgb[i + 2];

                    // grayscale simple (approx)
                    int gray = (r * 30 + g * 59 + b * 11) / 100;
                    tplGray[ti++] = (byte)gray;
                }
            }

            return true;
        }

        // ===== Tracking by SAD template match =====
        private static bool TryTemplateTrack(
            byte[] rgb, int w, int h,
            byte[] tplGray, int tplSize,
            int cx, int cy, int searchRadius,
            out int bestX, out int bestY, out int bestScore)
        {
            bestX = cx;
            bestY = cy;
            bestScore = int.MaxValue;

            int half = tplSize / 2;
            if (cx < 0 || cy < 0 || cx >= w || cy >= h) return false;

            int sx0 = Math.Max(half, cx - searchRadius);
            int sy0 = Math.Max(half, cy - searchRadius);
            int sx1 = Math.Min(w - 1 - half, cx + searchRadius);
            int sy1 = Math.Min(h - 1 - half, cy + searchRadius);

            if (sx0 > sx1 || sy0 > sy1) return false;

            // Score threshold (empêche de suivre n'importe quoi)
            // plus petit = meilleur. Ajustable.
            int maxAccept = tplSize * tplSize * 25; // tolérance (25 ~ sensible mais stable)

            for (int y = sy0; y <= sy1; y++)
            {
                for (int x = sx0; x <= sx1; x++)
                {
                    int score = SadAt(rgb, w, x - half, y - half, tplGray, tplSize);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            return bestScore <= maxAccept;
        }

        private static int SadAt(byte[] rgb, int w, int x0, int y0, byte[] tplGray, int size)
        {
            int score = 0;
            int ti = 0;

            for (int y = 0; y < size; y++)
            {
                int row = (y0 + y) * w * 3;
                for (int x = 0; x < size; x++)
                {
                    int i = row + (x0 + x) * 3;
                    byte r = rgb[i + 0];
                    byte g = rgb[i + 1];
                    byte b = rgb[i + 2];

                    int gray = (r * 30 + g * 59 + b * 11) / 100;
                    int diff = gray - tplGray[ti++];
                    if (diff < 0) diff = -diff;
                    score += diff;
                }
            }

            return score;
        }

        // ===== Depth median (raw ushort) =====
        private static ushort MedianDepthRaw(ushort[] depth, int w, int h, int cx, int cy, int radius)
        {
            var vals = new List<ushort>((2 * radius + 1) * (2 * radius + 1));

            for (int dy = -radius; dy <= radius; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= h) continue;

                int row = y * w;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= w) continue;

                    ushort raw = depth[row + x];
                    if (raw != 0) vals.Add(raw);
                }
            }

            if (vals.Count == 0) return 0;
            vals.Sort();
            return vals[vals.Count / 2];
        }

        // ===== RGB8 -> Bitmap 24bpp =====
        private static Bitmap RgbToBitmap24bpp(byte[] rgb, int w, int h)
        {
            // rgb = R,G,B...
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);

            try
            {
                int dstStride = data.Stride;
                int srcStride = w * 3;

                // écriture ligne par ligne en BGR pour Windows
                for (int y = 0; y < h; y++)
                {
                    IntPtr dstRow = data.Scan0 + y * dstStride;
                    int si = y * srcStride;

                    byte[] row = new byte[srcStride];
                    Buffer.BlockCopy(rgb, si, row, 0, srcStride);

                    for (int i = 0; i < row.Length; i += 3)
                    {
                        byte r = row[i + 0];
                        byte g = row[i + 1];
                        byte b = row[i + 2];
                        row[i + 0] = b;
                        row[i + 1] = g;
                        row[i + 2] = r;
                    }

                    Marshal.Copy(row, 0, dstRow, row.Length);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            return bmp;
        }

        private static void DrawGreenBox(Bitmap bmp, int px, int py)
        {
            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.Lime, 2);

            int x0 = px - BOX_HALF;
            int y0 = py - BOX_HALF;
            int size = BOX_HALF * 2;

            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x0 + size >= bmp.Width) x0 = Math.Max(0, bmp.Width - size - 1);
            if (y0 + size >= bmp.Height) y0 = Math.Max(0, bmp.Height - size - 1);

            g.DrawRectangle(pen, x0, y0, size, size);
            g.FillEllipse(Brushes.Lime, px - 2, py - 2, 5, 5);
        }

        // Clic (Zoom) -> pixel image
        private static (int x, int y) TranslateZoomMousePositionToImagePixel(PictureBox pb, Point mouse)
        {
            if (pb.Image == null) return (-1, -1);

            var img = pb.Image;
            float imageAspect = (float)img.Width / img.Height;
            float boxAspect = (float)pb.Width / pb.Height;

            int drawWidth, drawHeight;
            int offsetX = 0, offsetY = 0;

            if (imageAspect > boxAspect)
            {
                drawWidth = pb.Width;
                drawHeight = (int)(pb.Width / imageAspect);
                offsetY = (pb.Height - drawHeight) / 2;
            }
            else
            {
                drawHeight = pb.Height;
                drawWidth = (int)(pb.Height * imageAspect);
                offsetX = (pb.Width - drawWidth) / 2;
            }

            int x = mouse.X - offsetX;
            int y = mouse.Y - offsetY;

            if (x < 0 || y < 0 || x >= drawWidth || y >= drawHeight) return (-1, -1);

            int imgX = (int)(x * (img.Width / (float)drawWidth));
            int imgY = (int)(y * (img.Height / (float)drawHeight));
            return (imgX, imgY);
        }

        private void SafeUI(Action a)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(a);
                else a();
            }
            catch { }
        }

        private void SetStatus(string s)
        {
            SafeUI(() => distanceLabel.Text = s);
            Log(s);
        }

        private void Log(string s)
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {s}");
        }
    }
}