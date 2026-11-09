using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static FocusReader.Native.NativeMethods;

namespace FocusReader.Forms;

/// <summary>Cheap blur: shrink the image with prefiltering, then stretch it back with bilinear interpolation.</summary>
internal static class Blur
{
    // Before Windows 10 2004, WDA_EXCLUDEFROMCAPTURE acts like WDA_MONITOR (black in captures).
    public static bool CaptureExclusionSupported { get; } = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    public static int FactorFor(int strength) => Math.Clamp(strength, 1, 10) + 1;

    public static Bitmap Shrink(Bitmap source, int factor, Bitmap? reuse = null)
    {
        var size = new Size(Math.Max(1, source.Width / factor), Math.Max(1, source.Height / factor));
        var target = reuse is not null && reuse.Size == size
            ? reuse
            : new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        if (!ReferenceEquals(target, reuse))
            reuse?.Dispose();

        using var g = Graphics.FromImage(target);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(source, new Rectangle(Point.Empty, size));
        return target;
    }

    public static void Expand(Graphics g, Bitmap small, Rectangle destination,
        InterpolationMode mode = InterpolationMode.HighQualityBilinear)
    {
        g.InterpolationMode = mode;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(small, destination, 0, 0, small.Width, small.Height, GraphicsUnit.Pixel, attributes);
    }
}

/// <summary>
/// Blurred snapshot of the whole virtual screen, rendered on a background thread while blurring is
/// shown so capturing and scaling never stall the UI thread (which also runs the mouse hook).
/// The screen is captured straight into a small bitmap with GDI halftone averaging, then stretched
/// back with a fixed-point bilinear filter into a DIB that the window can BitBlt directly.
/// </summary>
internal sealed class BlurBackdrop : IDisposable
{
    private const int FrameIntervalMs = 150;

    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private IntPtr _frame;
    private Rectangle _frameArea;
    private int _session;
    private bool _running;
    private volatile int _factor = Blur.FactorFor(3);

    /// <summary>Raised on the worker thread after a new frame is available.</summary>
    public event Action? FrameReady;

    public double LastRenderMs { get; private set; }

    public void SetStrength(int strength) => _factor = Blur.FactorFor(strength);

    public void Start()
    {
        if (_running)
            return;

        _running = true;
        var session = Interlocked.Increment(ref _session);
        new Thread(() => Run(session)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Focus Reader blur" }.Start();
    }

    public void Stop()
    {
        if (!_running)
            return;

        _running = false;
        Interlocked.Increment(ref _session);
        _wake.Set();
        lock (_gate)
            _frame = IntPtr.Zero;
    }

    /// <summary>Copies the part of the current frame under <paramref name="clip"/> (window client coordinates).</summary>
    public bool Draw(IntPtr hdc, Rectangle clip, Point windowOrigin)
    {
        lock (_gate)
        {
            if (_frame == IntPtr.Zero)
                return false;

            var memory = CreateCompatibleDC(hdc);
            var previous = SelectObject(memory, _frame);
            BitBlt(hdc, clip.X, clip.Y, clip.Width, clip.Height, memory,
                clip.X + windowOrigin.X - _frameArea.X, clip.Y + windowOrigin.Y - _frameArea.Y, SRCCOPY);
            SelectObject(memory, previous);
            DeleteDC(memory);
            return true;
        }
    }

    public void Dispose()
    {
        Stop();
        _wake.Dispose();
    }

    private void Run(int session)
    {
        using var renderer = new Renderer();
        try
        {
            while (Volatile.Read(ref _session) == session)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var area = SystemInformation.VirtualScreen;
                var frame = renderer.Render(area, _factor);
                LastRenderMs = watch.Elapsed.TotalMilliseconds;

                if (frame != IntPtr.Zero)
                {
                    lock (_gate)
                    {
                        if (Volatile.Read(ref _session) != session)
                            break;

                        _frame = frame;
                        _frameArea = area;
                    }

                    renderer.Flip();
                    FrameReady?.Invoke();
                }

                // Never spend more than about half of one core on blurring, even on slow machines.
                var elapsed = (int)watch.ElapsedMilliseconds;
                _wake.WaitOne(Math.Max(Math.Max(30, elapsed), FrameIntervalMs - elapsed));
            }
        }
        finally
        {
            lock (_gate)
            {
                if (renderer.Owns(_frame))
                    _frame = IntPtr.Zero;
            }
        }
    }

    /// <summary>Owns the worker's GDI bitmaps: one small capture and two full-size frames (front/back).</summary>
    private sealed unsafe class Renderer : IDisposable
    {
        private readonly IntPtr[] _frames = new IntPtr[2];
        private readonly uint*[] _frameBits = new uint*[2];
        private int _back;
        private Size _frameSize;
        private IntPtr _small;
        private uint* _smallBits;
        private Size _smallSize;
        private int[] _x0 = [];
        private int[] _x1 = [];
        private int[] _wx = [];
        private uint[] _rowA = [];
        private uint[] _rowB = [];

        public bool Owns(IntPtr frame) => frame != IntPtr.Zero && (frame == _frames[0] || frame == _frames[1]);

        public void Flip() => _back ^= 1;

        public IntPtr Render(Rectangle area, int factor)
        {
            if (area.Width <= 0 || area.Height <= 0)
                return IntPtr.Zero;

            if (area.Size != _frameSize)
            {
                for (var i = 0; i < 2; i++)
                {
                    Free(ref _frames[i]);
                    _frames[i] = CreateDib(area.Size, out var bits);
                    _frameBits[i] = (uint*)bits;
                }

                _frameSize = area.Size;
                _smallSize = Size.Empty;
            }

            var small = new Size(Math.Max(1, area.Width / factor), Math.Max(1, area.Height / factor));
            if (small != _smallSize)
            {
                Free(ref _small);
                _small = CreateDib(small, out var bits);
                _smallBits = (uint*)bits;
                _smallSize = small;
                BuildTables(area.Width, small.Width);
            }

            if (_frames[_back] == IntPtr.Zero || _small == IntPtr.Zero || !Capture(area))
                return IntPtr.Zero;

            Expand(_frameBits[_back]);
            return _frames[_back];
        }

        public void Dispose()
        {
            Free(ref _frames[0]);
            Free(ref _frames[1]);
            Free(ref _small);
        }

        private bool Capture(Rectangle area)
        {
            var screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero)
                return false;

            var memory = CreateCompatibleDC(screen);
            var previous = SelectObject(memory, _small);
            SetStretchBltMode(memory, HALFTONE);
            SetBrushOrgEx(memory, 0, 0, IntPtr.Zero);

            // Fails on the secure desktop (UAC, lock screen); the next frame tries again.
            var ok = StretchBlt(memory, 0, 0, _smallSize.Width, _smallSize.Height,
                screen, area.X, area.Y, area.Width, area.Height, SRCCOPY);
            GdiFlush();

            SelectObject(memory, previous);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
            return ok;
        }

        private void BuildTables(int width, int smallWidth)
        {
            _x0 = new int[width];
            _x1 = new int[width];
            _wx = new int[width];
            for (var x = 0; x < width; x++)
                Sample(x, width, smallWidth, out _x0[x], out _x1[x], out _wx[x]);

            _rowA = new uint[width];
            _rowB = new uint[width];
        }

        private void Expand(uint* target)
        {
            int width = _frameSize.Width, height = _frameSize.Height;
            int rowAIndex = -1, rowBIndex = -1;

            for (var y = 0; y < height; y++)
            {
                Sample(y, height, _smallSize.Height, out var y0, out var y1, out var wy);

                if (rowAIndex != y0)
                {
                    if (rowBIndex == y0)
                    {
                        (_rowA, _rowB) = (_rowB, _rowA);
                        rowAIndex = y0;
                        rowBIndex = -1;
                    }
                    else
                    {
                        StretchRow(y0, _rowA);
                        rowAIndex = y0;
                    }
                }

                if (rowBIndex != y1)
                {
                    StretchRow(y1, _rowB);
                    rowBIndex = y1;
                }

                var output = target + (long)y * width;
                fixed (uint* a = _rowA, b = _rowB)
                {
                    for (var x = 0; x < width; x++)
                        output[x] = Lerp(a[x], b[x], wy);
                }
            }
        }

        private void StretchRow(int smallY, uint[] row)
        {
            var source = _smallBits + (long)smallY * _smallSize.Width;
            for (var x = 0; x < row.Length; x++)
                row[x] = Lerp(source[_x0[x]], source[_x1[x]], _wx[x]);
        }

        // Maps a destination pixel to its two source neighbours and the 8-bit weight of the second one.
        private static void Sample(int i, int length, int smallLength, out int i0, out int i1, out int weight)
        {
            var position = (int)Math.Max(0, ((2L * i + 1) * smallLength * 128 / length) - 128);
            i0 = Math.Min(position >> 8, smallLength - 1);
            i1 = Math.Min(i0 + 1, smallLength - 1);
            weight = position & 0xFF;
        }

        private static uint Lerp(uint a, uint b, int weight)
        {
            var inverse = (uint)(256 - weight);
            var w = (uint)weight;
            var redBlue = (((a & 0x00FF00FF) * inverse + (b & 0x00FF00FF) * w) >> 8) & 0x00FF00FF;
            var alphaGreen = (((a >> 8) & 0x00FF00FF) * inverse + ((b >> 8) & 0x00FF00FF) * w) & 0xFF00FF00;
            return redBlue | alphaGreen;
        }

        private static IntPtr CreateDib(Size size, out IntPtr bits)
        {
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = size.Width,
                biHeight = -size.Height,
                biPlanes = 1,
                biBitCount = 32,
            };
            return CreateDIBSection(IntPtr.Zero, ref header, 0, out bits, IntPtr.Zero, 0);
        }

        private static void Free(ref IntPtr bitmap)
        {
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            bitmap = IntPtr.Zero;
        }
    }
}

/// <summary>
/// Click-through window covering the virtual screen with the blurred backdrop. The highlight area is
/// cut out with a window region, so moving the highlight only changes the region and needs no repaint.
/// </summary>
internal sealed class BlurWindow : Form
{
    private readonly BlurBackdrop _backdrop;
    private Rectangle _hole;

    public BlurWindow(BlurBackdrop backdrop)
    {
        _backdrop = backdrop;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ControlBox = false;
        TopMost = true;
        BackColor = Color.Gray;
        Text = "Focus Reader Blur";
        Bounds = new Rectangle(-32000, -32000, 1, 1);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
    }

    /// <summary>False when Windows can't hide this window from screen capture (older than Windows 10 2004).</summary>
    public bool ExcludedFromCapture { get; private set; }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void Cover(Rectangle screen, Rectangle hole)
    {
        if (!IsHandleCreated)
            return;

        if (screen != Bounds)
        {
            SetWindowPos(Handle, HWND_TOPMOST, screen.X, screen.Y, screen.Width, screen.Height,
                SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            _hole = Rectangle.Empty;
        }

        hole.Offset(-screen.X, -screen.Y);
        if (hole == _hole)
            return;

        _hole = hole;
        var region = CreateRectRgn(0, 0, screen.Width, screen.Height);
        var cutout = CreateRectRgn(hole.Left, hole.Top, hole.Right, hole.Bottom);
        CombineRgn(region, region, cutout, RGN_DIFF);
        DeleteObject(cutout);
        if (SetWindowRgn(Handle, region, true) == 0)
            DeleteObject(region);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);
        ExcludedFromCapture = Blur.CaptureExclusionSupported && SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var hdc = e.Graphics.GetHdc();
        try
        {
            _backdrop.Draw(hdc, e.ClipRectangle, Location);
        }
        finally
        {
            e.Graphics.ReleaseHdc(hdc);
        }
    }

    protected override void WndProc(ref Message m)
    {
        // Position and size are managed by OverlayForm; ignore the DPI-suggested rectangle.
        if (m.Msg == WM_DPICHANGED)
            return;

        base.WndProc(ref m);
    }
}
