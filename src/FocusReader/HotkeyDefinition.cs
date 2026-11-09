using FocusReader.Native;

namespace FocusReader;

internal sealed record HotkeyDefinition(Keys Key, bool Control, bool Alt, bool Shift, bool Win)
{
    public static HotkeyDefinition Default { get; } = new(Keys.R, Control: true, Alt: true, Shift: false, Win: false);

    public uint Modifiers =>
        (Control ? NativeMethods.MOD_CONTROL : 0) |
        (Alt ? NativeMethods.MOD_ALT : 0) |
        (Shift ? NativeMethods.MOD_SHIFT : 0) |
        (Win ? NativeMethods.MOD_WIN : 0);

    public bool HasModifier => Control || Alt || Shift || Win;

    public static bool IsFunctionKey(Keys key) => key is >= Keys.F1 and <= Keys.F24;

    public bool IsValid => Key != Keys.None && (HasModifier || IsFunctionKey(Key));

    public override string ToString()
    {
        var parts = new List<string>();
        if (Control) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    private static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (key - Keys.NumPad0),
        Keys.Space => "Espaço",
        Keys.Return => "Enter",
        Keys.Escape => "Esc",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.PageUp => "PgUp",
        Keys.PageDown => "PgDn",
        _ => key.ToString(),
    };
}
