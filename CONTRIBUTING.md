# Contributing

Thanks for your interest in Focus Reader! Bug reports, ideas and pull requests are welcome.

## Reporting bugs and ideas

Open an [issue](../../issues/new/choose). For bugs, include the version shown in the tray menu → **Sobre...**, your Windows version and your display setup (number of monitors and scale), since most behavior depends on them.

## Development

- Requires Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The project also builds on Linux/macOS (`EnableWindowsTargeting`), but only runs on Windows.
- Run: `dotnet run --project src/FocusReader`
- Build: `dotnet build FocusReader.sln -c Release` must finish without warnings.
- Keep changes focused and follow the existing code style. Native calls go in `src/FocusReader/Native/NativeMethods.cs`.
- The UI text is in Portuguese (pt-BR).

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Describe what changed and how you tested it (Windows version, displays/scale).
3. CI (GitHub Actions on `windows-latest`) must pass.

## Releases

Maintainers publish a release by pushing a tag such as `v1.2.3` (or `v1.2.3-beta.1` for a prerelease). CI builds the executables, stamps the version and build number, and attaches the zips to the GitHub release.
