# Third-party notices

SocketScope source code is licensed under MIT. The following third-party components are
used by, embedded in, or distributed with builds. Package versions are centrally pinned
in `Directory.Packages.props` and locked per project.

## Runtime packages

| Component | Version | License | Purpose/source |
|---|---:|---|---|
| Avalonia (`Avalonia`, Desktop, Win32, X11, FreeDesktop, Native, Skia, HarfBuzz, DataGrid, Fluent theme, Remote.Protocol) | 12.1.1 | MIT | Cross-platform desktop UI, <https://github.com/AvaloniaUI/Avalonia> |
| Avalonia.BuildServices | 11.3.2 | MIT | Avalonia build tooling |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | BSD 3-Clause-style ANGLE license | Windows graphics runtime, <https://github.com/AvaloniaUI/angle> |
| SkiaSharp and native assets | 3.119.4 | MIT | Rendering dependency, <https://github.com/mono/SkiaSharp> |
| HarfBuzzSharp and native assets | 8.3.1.3 | MIT | Text shaping dependency, <https://github.com/mono/SkiaSharp> |
| MicroCom.Runtime | 0.11.6 | MIT | Native COM interop support |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Linux D-Bus protocol support, <https://github.com/tmds/Tmds.DBus> |
| Microsoft.Extensions.DependencyInjection and abstractions | 10.0.11 | MIT | Composition root, <https://github.com/dotnet/runtime> |
| Microsoft.Extensions.Logging, Options, and Primitives | 10.0.11 | MIT | Structured local event pipeline, <https://github.com/dotnet/runtime> |

Self-contained release assets also include the Microsoft .NET runtime and its native
dependencies under their respective licenses and notices. The release SBOM is the
authoritative per-artifact component inventory. Microsoft .NET notices are maintained
at <https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT>.

## Locally embedded font

SocketScope embeds the owner-provided `Inter-VariableFont_opsz,wght.ttf` from the
Google Fonts Inter distribution under the SIL Open Font License 1.1. It is packaged as
`Inter-Variable.ttf` and loaded only from the application package.

- Original project: <https://github.com/rsms/inter>
- Imported from the owner-provided local download: 2026-08-13
- Size: `874708` bytes
- SHA-256: `0be2399ea925f1f83ff974764761da9860ec50742ed29a5d4c1ffd0c5c7ac3a8`
- License copy: `src/OeXYZ.SocketScope.Desktop/Assets/Fonts/OFL-Inter.txt`
- License SHA-256: `3c8d13d97ea5eb62868833a377034c6be6a9833e768d32abec2a767a8101e4bc`

Additional provenance is recorded in the adjacent font `README.md`.

The SocketScope application icon under `Assets/Icons` is original project artwork,
contains no third-party elements, and is covered by the repository MIT license.

## Test-only packages

| Component | Version | License |
|---|---:|---|
| xUnit.net v3 and runner | 3.2.2 / 3.1.5 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 18.8.1 | MIT |
| Avalonia.Headless.XUnit | 12.1.1 | MIT |

## License texts

- MIT: <https://opensource.org/license/mit>
- Apache License 2.0: <https://www.apache.org/licenses/LICENSE-2.0>
- SIL Open Font License 1.1: included with the font.
- ANGLE license: included in the corresponding NuGet package and requires retention of
  its copyright, conditions, and disclaimer in binary redistribution.

If a generated SBOM and this human-maintained notice disagree, stop the release and
review the dependency/notice diff before distribution.
