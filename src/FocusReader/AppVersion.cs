using System.Reflection;

namespace FocusReader;

/// <summary>Version stamped into the assembly at build time (see the versioning block in FocusReader.csproj).</summary>
internal static class AppVersion
{
    private static readonly Assembly Assembly = typeof(AppVersion).Assembly;

    public static string Version { get; } =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public static int BuildNumber { get; } = int.TryParse(Metadata("BuildNumber"), out var build) ? build : 0;

    public static string? Commit { get; } = Metadata("Commit");

    /// <summary>For example "0.1.0 (build 57)", or "0.1.0 (build local)" for a build made outside CI.</summary>
    public static string Display => $"{Version} (build {(BuildNumber > 0 ? BuildNumber.ToString() : "local")})";

    private static string? Metadata(string key) =>
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value
            ? value
            : null;
}
