using FocusReader.Native;
using Microsoft.Win32;
using static FocusReader.Native.NativeMethods;

namespace FocusReader;

/// <summary>
/// Replaces the system-wide cursors while reading mode is on and restores the
/// user's cursor scheme afterwards (SPI_SETCURSORS reloads it from the registry).
/// </summary>
internal static class SystemCursorManager
{
    private static readonly uint[] TargetCursors = [OCR_NORMAL, OCR_IBEAM, OCR_HAND];
    private const string CursorsKey = @"HKEY_CURRENT_USER\Control Panel\Cursors";
    private static bool _applied;

    private static string MarkerPath => Path.Combine(SettingsStore.Folder, "cursor-applied.marker");

    public static void Apply(CursorChoice choice, string? customPath)
    {
        Restore();
        if (choice == CursorChoice.Unchanged)
            return;

        foreach (var target in TargetCursors)
        {
            // SetSystemCursor takes ownership of (and destroys) the handle, so each target needs its own copy.
            var handle = CreateCursorHandle(choice, customPath);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("Não foi possível carregar o cursor selecionado.");

            if (!SetSystemCursor(handle, target))
            {
                DestroyCursor(handle);
                continue;
            }

            MarkApplied();
        }

        if (!_applied)
            throw new InvalidOperationException("Não foi possível substituir o cursor do sistema.");
    }

    public static void Restore()
    {
        if (!_applied)
            return;

        if (!SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0))
            return;

        _applied = false;
        TryDeleteMarker();
    }

    /// <summary>Restores the cursor scheme if a previous run ended without restoring it.</summary>
    public static void RecoverFromPreviousRun()
    {
        if (!File.Exists(MarkerPath))
            return;

        if (SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0))
            TryDeleteMarker();
    }

    public static bool CanLoad(CursorChoice choice, string? customPath)
    {
        if (choice == CursorChoice.Unchanged)
            return true;

        var handle = CreateCursorHandle(choice, customPath);
        if (handle == IntPtr.Zero)
            return false;

        DestroyCursor(handle);
        return true;
    }

    private static IntPtr CreateCursorHandle(CursorChoice choice, string? customPath)
    {
        // Loading at the user's pointer size avoids Windows upscaling a 32px image
        // when "Mouse pointer size" is increased in accessibility settings.
        var size = TargetCursorSize();

        if (choice == CursorChoice.Custom)
        {
            if (string.IsNullOrWhiteSpace(customPath))
                return IntPtr.Zero;

            var custom = LoadImage(IntPtr.Zero, customPath, IMAGE_CURSOR, size, size, LR_LOADFROMFILE);
            return custom != IntPtr.Zero ? custom : LoadCursorFromFile(customPath);
        }

        var id = CursorChoices.SystemCursorId(choice) ?? CursorChoices.SystemCursorId(CursorChoice.Hand)!.Value;

        var schemeFile = SchemeCursorFile(choice);
        if (schemeFile is not null)
        {
            var fromScheme = LoadImage(IntPtr.Zero, schemeFile, IMAGE_CURSOR, size, size, LR_LOADFROMFILE);
            if (fromScheme != IntPtr.Zero)
                return fromScheme;
        }

        var oem = LoadImage(IntPtr.Zero, new IntPtr(id), IMAGE_CURSOR, size, size, LR_DEFAULTCOLOR);
        if (oem != IntPtr.Zero)
            return oem;

        var shared = LoadCursor(IntPtr.Zero, new IntPtr(id));
        return shared == IntPtr.Zero ? IntPtr.Zero : CopyIcon(shared);
    }

    /// <summary>Pointer size chosen in Windows settings, scaled to the highest monitor DPI.</summary>
    private static int TargetCursorSize()
    {
        var baseSize = 32;
        try
        {
            if (Registry.GetValue(CursorsKey, "CursorBaseSize", null) is int configured && configured > 0)
                baseSize = configured;
        }
        catch (Exception)
        {
        }

        var dpi = 96u;
        foreach (var screen in Screen.AllScreens)
        {
            var center = new POINT
            {
                X = screen.Bounds.Left + screen.Bounds.Width / 2,
                Y = screen.Bounds.Top + screen.Bounds.Height / 2,
            };
            var monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
            try
            {
                if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
                    dpi = Math.Max(dpi, dpiX);
            }
            catch (Exception)
            {
            }
        }

        return Math.Clamp((int)Math.Round(baseSize * dpi / 96.0), 16, 256);
    }

    /// <summary>File used by the user's current cursor scheme, if any.</summary>
    private static string? SchemeCursorFile(CursorChoice choice)
    {
        var valueName = choice switch
        {
            CursorChoice.Hand => "Hand",
            CursorChoice.Arrow => "Arrow",
            CursorChoice.Cross => "Crosshair",
            CursorChoice.UpArrow => "UpArrow",
            CursorChoice.IBeam => "IBeam",
            CursorChoice.Help => "Help",
            _ => null,
        };
        if (valueName is null)
            return null;

        try
        {
            if (Registry.GetValue(CursorsKey, valueName, null) is not string path || string.IsNullOrWhiteSpace(path))
                return null;

            path = Environment.ExpandEnvironmentVariables(path);
            return File.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void MarkApplied()
    {
        if (_applied)
            return;

        _applied = true;
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.WriteAllText(MarkerPath, DateTime.Now.ToString("O"));
        }
        catch (Exception)
        {
            // Best effort: the marker only helps recovering after a crash.
        }
    }

    private static void TryDeleteMarker()
    {
        try
        {
            File.Delete(MarkerPath);
        }
        catch (Exception)
        {
        }
    }
}
