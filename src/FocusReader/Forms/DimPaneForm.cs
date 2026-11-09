using static FocusReader.Native.NativeMethods;

namespace FocusReader.Forms;

/// <summary>Click-through translucent black rectangle; four of them dim the screen around the highlight.</summary>
internal sealed class DimPaneForm : Form
{
    private byte _alpha = 64;

    public DimPaneForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ControlBox = false;
        TopMost = true;
        BackColor = Color.Black;
        Text = "Focus Reader Dim";
        Bounds = new Rectangle(-32000, -32000, 1, 1);
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

    public void SetAlpha(byte alpha)
    {
        _alpha = alpha;
        if (IsHandleCreated)
            SetLayeredWindowAttributes(Handle, 0, _alpha, LWA_ALPHA);
    }

    public void Place(int x, int y, int width, int height)
    {
        if (IsHandleCreated)
            SetWindowPos(Handle, HWND_TOPMOST, x, y, Math.Max(width, 0), Math.Max(height, 0),
                SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetLayeredWindowAttributes(Handle, 0, _alpha, LWA_ALPHA);
    }

    protected override void WndProc(ref Message m)
    {
        // Position and size are managed by OverlayForm; ignore the DPI-suggested rectangle.
        if (m.Msg == WM_DPICHANGED)
            return;

        base.WndProc(ref m);
    }
}
