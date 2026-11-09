using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusReader;

internal sealed class AppSettings
{
    public const int MinOpacity = 5;
    public const int MaxOpacity = 95;

    public CursorChoice Cursor { get; set; } = CursorChoice.Hand;
    public string? CustomCursorPath { get; set; }
    public string OverlayColorHex { get; set; } = "#FFE600";
    public int OverlayOpacity { get; set; } = 35;
    public int OverlayWidth { get; set; } = 85;
    public int OverlayHeight { get; set; } = 30;
    public int CursorOffset { get; set; } = 2;
    public bool VerticalStabilization { get; set; } = true;
    public int VerticalTolerance { get; set; } = 18;
    public HotkeyDefinition? Hotkey { get; set; } = HotkeyDefinition.Default;

    [JsonIgnore]
    public Color OverlayColor
    {
        get
        {
            try
            {
                var c = ColorTranslator.FromHtml(OverlayColorHex);
                return c.IsEmpty ? Color.Yellow : Color.FromArgb(c.R, c.G, c.B);
            }
            catch (Exception)
            {
                return Color.Yellow;
            }
        }
        set => OverlayColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public void Normalize()
    {
        OverlayOpacity = Math.Clamp(OverlayOpacity, MinOpacity, MaxOpacity);
        OverlayWidth = Math.Clamp(OverlayWidth, 40, 4000);
        OverlayHeight = Math.Clamp(OverlayHeight, 6, 600);
        CursorOffset = Math.Clamp(CursorOffset, -200, 200);
        VerticalTolerance = Math.Clamp(VerticalTolerance, 1, 400);
        OverlayColor = OverlayColor;
        if (!Enum.IsDefined(Cursor))
            Cursor = CursorChoice.Hand;
        if (Hotkey is { IsValid: false })
            Hotkey = HotkeyDefinition.Default;
    }

    public AppSettings Clone() => SettingsStore.Clone(this);
}

internal static class SettingsStore
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusReader");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
        }
        catch (Exception)
        {
            settings = null;
        }

        settings ??= new AppSettings();
        settings.Normalize();
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public static AppSettings Clone(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Options), Options)!;
}
