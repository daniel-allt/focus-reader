using FocusReader.Native;

namespace FocusReader.Forms;

/// <summary>Read-only text box that captures a key combination when focused.</summary>
internal sealed class HotkeyTextBox : TextBox
{
    private HotkeyDefinition? _hotkey;

    public event EventHandler? HotkeyChanged;

    public HotkeyTextBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        ShortcutsEnabled = false;
        TextAlign = HorizontalAlignment.Center;
        UpdateText();
    }

    public HotkeyDefinition? Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            UpdateText();
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Tab or (Keys.Tab | Keys.Shift))
            return base.ProcessCmdKey(ref msg, keyData);

        HandleKey(keyData);
        return true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        UpdateText();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        UpdateText();
    }

    private void HandleKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var ctrl = keyData.HasFlag(Keys.Control);
        var alt = keyData.HasFlag(Keys.Alt);
        var shift = keyData.HasFlag(Keys.Shift);
        var win = IsWinDown();

        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            var partial = new List<string>();
            if (ctrl) partial.Add("Ctrl");
            if (alt) partial.Add("Alt");
            if (shift) partial.Add("Shift");
            if (win) partial.Add("Win");
            Text = string.Join(" + ", partial) + " + ...";
            return;
        }

        if (key is Keys.Back or Keys.Delete && !ctrl && !alt && !shift && !win)
        {
            Hotkey = null;
            return;
        }

        var candidate = new HotkeyDefinition(key, ctrl, alt, shift, win);
        if (!candidate.IsValid)
        {
            Text = "Use Ctrl, Alt, Shift ou Win + tecla";
            return;
        }

        Hotkey = candidate;
    }

    private void UpdateText() => Text = _hotkey?.ToString() ?? "(nenhum)";

    private static bool IsWinDown() =>
        (NativeMethods.GetKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0 ||
        (NativeMethods.GetKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0;
}
