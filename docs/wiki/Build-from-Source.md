# Build from source

Windows only. WPF cannot be built on Linux or macOS at all — the Windows Desktop SDK is not
available there.

| Need | Version |
| --- | --- |
| .NET SDK | 10.0 |
| OS | Windows 10/11 64-bit |

```powershell
dotnet build CspPaletteCompanion.sln -c Release
dotnet test  CspPaletteCompanion.sln -c Release --no-build
```

`TreatWarningsAsErrors` is on. A warning fails the build.

## Release artifacts

```powershell
tools\publish-local.ps1 -Version 1.0.0
```

Writes to `dist\release\` with `SHA256SUMS.txt`. Same flags as CI, so a hand-cut release and
a tagged one produce identical files.

| Artifact | Size | Flags |
| --- | --- | --- |
| `…-needs-dotnet10.exe` | 1.1 MiB | `--self-contained false -p:PublishSingleFile=true` |
| `…-standalone.exe` | 72.3 MiB | `--self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeAllContentForSelfExtract=true` |

`IncludeAllContentForSelfExtract` is load-bearing. Without it the publish leaves five native
WPF DLLs (`D3DCompiler_47_cor3`, `wpfgfx_cor3`, `PresentationNative_cor3`, `PenImc_cor3`,
`vcruntime140_cor3`) loose beside the exe, so the "standalone" download does not run on its
own. It also folds in `docs\selection-canvas-setup.md`, which the in-app setup-guide button
reads from `AppContext.BaseDirectory`.

**Trimming and NativeAOT are not possible here.** `-p:PublishTrimmed=true` fails with
`NETSDK1175`: both apps reference WinForms, for the tray icon and for ZXing. Do not add
either flag.

## Versioning

`Directory.Build.props` defaults to `0.1.0`; a `v*` tag drives the real version through
`-p:Version=`. The app csproj carries no `<Version>` — a project-level value silently wins
over the command line.

## Theme.xaml

`src\CspPaletteCompanion.App\Theme\Theme.xaml` is byte-identical to the copy in the CSP Mux
repository. A `SuiteSyncCheck` build target fails on drift; `tools\suite-sync.ps1 -Mode Push`
reconciles the two working trees when both are checked out side by side.

## CI

| Job | Runner | Covers |
| --- | --- | --- |
| `windows` | Windows | the WPF app, all tests, every release artifact |

`release.yml` runs on a `v*` tag and calls `publish-local.ps1`, then creates the release
through the Forgejo API with `curl`. Auth header is `token`, not `Bearer`.
