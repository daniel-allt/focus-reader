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
        {
            if (_frame != IntPtr.Zero)
                DeleteObject(_frame);
            _frame = IntPtr.Zero;
        }
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
        Bitmap? capture = null;
        Bitmap? small = null;
        try
        {
            while (Volatile.Read(ref _session) == session)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                if (Render(session, ref capture, ref small))
                {
                    LastRenderMs = watch.Elapsed.TotalMilliseconds;
                    FrameReady?.Invoke();
                }

                _wake.WaitOne(Math.Max(30, FrameIntervalMs - (int)watch.ElapsedMilliseconds));
            }
        }
        finally
        {
            capture?.Dispose();
            small?.Dispose();
        }
    }

    private bool Render(int session, ref Bitmap? capture, ref Bitmap? small)
    {
        var area = SystemInformation.VirtualScreen;
        if (area.Width <= 0 || area.Height <= 0)
            return false;

        if (capture is null || capture.Size != area.Size)
        {
            capture?.Dispose();
            capture = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
        }

        try
        {
            using (var g = Graphics.FromImage(capture))
                g.CopyFromScreen(area.Location, Point.Empty, area.Size);

            small = Blur.Shrink(capture, _factor, small);
            using (var g = Graphics.FromImage(capture))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                Blur.Expand(g, small, new Rectangle(Point.Empty, area.Size), InterpolationMode.Bilinear);
            }
        }
        catch (Exception)
        {
            // Capturing fails on the secure desktop (UAC, lock screen); try again on the next frame.
            return false;
        }

        var frame = capture.GetHbitmap();
        lock (_gate)
        {
            if (Volatile.Read(ref _session) != session)
            {
                DeleteObject(frame);
                return false;
            }

            if (_frame != IntPtr.Zero)
                DeleteObject(_frame);
            _frame = frame;
            _frameArea = area;
        }

        return true;
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
