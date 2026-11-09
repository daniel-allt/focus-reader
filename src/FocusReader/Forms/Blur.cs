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

    public static void Expand(Graphics g, Bitmap small, Rectangle destination)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(small, destination, 0, 0, small.Width, small.Height, GraphicsUnit.Pixel, attributes);
    }
}

/// <summary>Blurred snapshot of the whole virtual screen, refreshed periodically while blurring is shown.</summary>
internal sealed class BlurBackdrop : IDisposable
{
    private Bitmap? _frame;
    private Bitmap? _small;
    private Rectangle _area;
    private int _factor = Blur.FactorFor(3);

    public void SetStrength(int strength) => _factor = Blur.FactorFor(strength);

    public void Refresh()
    {
        var area = SystemInformation.VirtualScreen;
        if (area.Width <= 0 || area.Height <= 0)
            return;

        if (_frame is null || _frame.Size != area.Size)
        {
            _frame?.Dispose();
            _frame = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
        }

        try
        {
            using var g = Graphics.FromImage(_frame);
            g.CopyFromScreen(area.Location, Point.Empty, area.Size);
        }
        catch (Exception)
        {
            // Capturing fails on the secure desktop (UAC, lock screen); keep the previous frame.
            return;
        }

        _small = Blur.Shrink(_frame, _factor, _small);
        using (var g = Graphics.FromImage(_frame))
            Blur.Expand(g, _small, new Rectangle(Point.Empty, area.Size));
        _area = area;
    }

    public void Draw(Graphics g, Rectangle screenRect)
    {
        if (_frame is null || _area.IsEmpty)
        {
            g.Clear(Color.Gray);
            return;
        }

        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(_frame, new Rectangle(Point.Empty, screenRect.Size),
            new Rectangle(screenRect.X - _area.X, screenRect.Y - _area.Y, screenRect.Width, screenRect.Height),
            GraphicsUnit.Pixel);
    }

    public void Dispose()
    {
        _frame?.Dispose();
        _small?.Dispose();
    }
}

/// <summary>Click-through window showing its part of the blurred backdrop; four of them surround the highlight.</summary>
internal sealed class BlurPaneForm : Form
{
    private readonly BlurBackdrop _backdrop;

    public BlurPaneForm(BlurBackdrop backdrop)
    {
        _backdrop = backdrop;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ControlBox = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.Gray;
        Text = "Focus Reader Blur";
        Bounds = new Rectangle(-32000, -32000, 1, 1);
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

    public void Place(Rectangle bounds)
    {
        bounds.Width = Math.Max(bounds.Width, 0);
        bounds.Height = Math.Max(bounds.Height, 0);
        if (!IsHandleCreated || bounds == Bounds)
            return;

        SetWindowPos(Handle, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        Invalidate();
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

    protected override void OnPaint(PaintEventArgs e) => _backdrop.Draw(e.Graphics, Bounds);

    protected override void WndProc(ref Message m)
    {
        // Position and size are managed by OverlayForm; ignore the DPI-suggested rectangle.
        if (m.Msg == WM_DPICHANGED)
            return;

        base.WndProc(ref m);
    }
}
