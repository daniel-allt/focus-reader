using System.Runtime.InteropServices;
using FocusReader.Native;
using static FocusReader.Native.NativeMethods;

namespace FocusReader.Forms;

/// <summary>
/// Borderless, click-through, semi-transparent window that stays just above the cursor
/// tip, acting as a highlighter over the line being read.
/// </summary>
internal sealed class OverlayForm : Form
{
    private const double SlideFactor = 0.35;

    private readonly System.Windows.Forms.Timer _fallbackTimer = new() { Interval = 50 };
    private readonly System.Windows.Forms.Timer _slideTimer = new() { Interval = 10 };
    private readonly LowLevelMouseProc _hookProc;
    private IntPtr _mouseHook;

    private Point _lastCursor = new(int.MinValue, int.MinValue);
    private IntPtr _lastMonitor;
    private double _scale = 1.0;

    private int _baseWidth = 85;
    private int _baseHeight = 30;
    private int _baseOffset = 2;
    private bool _stabilize = true;
    private int _baseTolerance = 18;
    private int _followMs = 800;
    private long _followUntil;

    private int _cursorX;
    private int _anchorY;
    private double? _currentY;
    private byte _alpha = 90;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ControlBox = false;
        TopMost = true;
        Text = "Focus Reader Overlay";
        Bounds = new Rectangle(-32000, -32000, 1, 1);

        _hookProc = MouseHookCallback;
        _fallbackTimer.Tick += (_, _) => MoveTo(Cursor.Position);
        _slideTimer.Tick += (_, _) => Slide();
    }

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

    public void ApplySettings(AppSettings settings)
    {
        BackColor = settings.OverlayColor;
        _alpha = (byte)Math.Round(settings.OverlayOpacity * 255 / 100.0);
        _baseWidth = settings.OverlayWidth;
        _baseHeight = settings.OverlayHeight;
        _baseOffset = settings.CursorOffset;
        _stabilize = settings.VerticalStabilization;
        _baseTolerance = settings.VerticalTolerance;
        _followMs = settings.VerticalFollowMs;
        ApplyAlpha();
        ForceReposition();
    }

    public void StartFollowing()
    {
        _ = Handle;
        ForceReposition();
        if (!Visible)
            Show();

        if (_mouseHook == IntPtr.Zero)
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);

        _fallbackTimer.Start();
    }

    public void StopFollowing()
    {
        _fallbackTimer.Stop();
        _slideTimer.Stop();
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        Hide();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyAlpha();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DPICHANGED)
        {
            // Size is managed manually per monitor; ignore the suggested rectangle.
            _lastMonitor = IntPtr.Zero;
            ForceReposition();
            return;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopFollowing();
            _fallbackTimer.Dispose();
            _slideTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_MOUSEMOVE)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var screen = SystemInformation.VirtualScreen;
            MoveTo(new Point(
                Math.Clamp(info.pt.X, screen.Left, screen.Right - 1),
                Math.Clamp(info.pt.Y, screen.Top, screen.Bottom - 1)));
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void ForceReposition()
    {
        _lastCursor = new Point(int.MinValue, int.MinValue);
        _currentY = null;
        _slideTimer.Stop();
        if (IsHandleCreated)
            MoveTo(Cursor.Position);
    }

    private void MoveTo(Point cursor)
    {
        if (cursor == _lastCursor || !IsHandleCreated)
            return;

        var dx = Math.Abs((long)cursor.X - _lastCursor.X);
        var dy = Math.Abs((long)cursor.Y - _lastCursor.Y);
        _lastCursor = cursor;
        UpdateScale(cursor);
        _cursorX = cursor.X;

        // Vertical dead zone: the highlight keeps its line until the cursor drifts past the tolerance.
        // Then it follows the cursor freely for a while (renewed by vertical moves) so the line can be
        // fine-tuned, and locks again wherever the cursor settled.
        var now = Environment.TickCount64;
        if (!_stabilize || _currentY is null)
        {
            _anchorY = cursor.Y;
            _currentY = cursor.Y;
            _followUntil = 0;
            _slideTimer.Stop();
        }
        else if (now < _followUntil)
        {
            _anchorY = cursor.Y;
            if (dy > 0 && dy >= dx)
                _followUntil = now + _followMs;
            _slideTimer.Start();
        }
        else if (Math.Abs(cursor.Y - _anchorY) > Scale(_baseTolerance))
        {
            _anchorY = cursor.Y;
            _followUntil = now + _followMs;
            _slideTimer.Start();
        }

        Place();
    }

    private void Slide()
    {
        if (_currentY is not { } current)
        {
            _slideTimer.Stop();
            return;
        }

        var remaining = _anchorY - current;
        if (Math.Abs(remaining) < 0.5)
        {
            _currentY = _anchorY;
            _slideTimer.Stop();
        }
        else
        {
            _currentY = current + remaining * SlideFactor;
        }

        Place();
    }

    private void Place()
    {
        if (!IsHandleCreated || _currentY is not { } y)
            return;

        var width = Scale(_baseWidth);
        var height = Scale(_baseHeight);
        var offset = Scale(_baseOffset);

        SetWindowPos(Handle, HWND_TOPMOST,
            _cursorX - width / 2, (int)Math.Round(y) - offset - height, width, height,
            SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private void UpdateScale(Point cursor)
    {
        var monitor = MonitorFromPoint(new POINT { X = cursor.X, Y = cursor.Y }, MONITOR_DEFAULTTONEAREST);
        if (monitor == _lastMonitor)
            return;

        _lastMonitor = monitor;
        try
        {
            _scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0
                ? dpiX / 96.0
                : 1.0;
        }
        catch (Exception)
        {
            _scale = 1.0;
        }
    }

    private int Scale(int value) => (int)Math.Round(value * _scale);

    private void ApplyAlpha()
    {
        if (IsHandleCreated)
            SetLayeredWindowAttributes(Handle, 0, _alpha, LWA_ALPHA);
    }
}
