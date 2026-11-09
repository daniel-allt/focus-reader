using FocusReader.Native;

namespace FocusReader;

/// <summary>Registers a system-wide hotkey on a hidden message-only window.</summary>
internal sealed class GlobalHotkey : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x4652;
    private bool _registered;

    public event EventHandler? Pressed;

    public GlobalHotkey()
    {
        CreateHandle(new CreateParams { Parent = NativeMethods.HWND_MESSAGE });
    }

    public bool Register(HotkeyDefinition? hotkey)
    {
        Unregister();
        if (hotkey is null || !hotkey.IsValid)
            return hotkey is null;

        _registered = NativeMethods.RegisterHotKey(
            Handle, HotkeyId, hotkey.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)hotkey.Key);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered)
            return;

        NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}
