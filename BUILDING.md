# Building DashStats

## Requirements

- Windows 10 or 11, x64
- **.NET SDK 10.0** (built and tested with **10.0.401**). Install with:
  ```
  winget install Microsoft.DotNet.SDK.10
  ```
  No Visual Studio needed. Any editor works (VS Code with the C# extension is fine).
- Git

Nothing else: NuGet restores LibreHardwareMonitorLib on first build, and the two third-party executables are already in `vendor/`.

## Build the single-file exe

From the repo root:

```powershell
dotnet publish src\DashStats\DashStats.csproj -c Release -o dist
```

That produces `dist\DashStats.exe`, roughly **174 MB**, self-contained: the .NET runtime, WPF and every dependency are inside it, so the target PC needs nothing installed. The relevant settings are all in `src/DashStats/DashStats.csproj`:

| Property | Value | Why |
|---|---|---|
| `TargetFramework` | `net10.0-windows` | WPF + WinForms (tray icon only) |
| `RuntimeIdentifier` | `win-x64` | |
| `SelfContained` | `true` | no .NET install needed on the target |
| `PublishSingleFile` | `true` | one exe |
| `IncludeNativeLibrariesForSelfExtract` | `true` | native DLLs go inside the exe too |
| `EnableCompressionInSingleFile` | `false` | compressed is ~78 MB but gets unpacked into RAM at startup; uncompressed is memory-mapped, so the running app is lighter |
| `ApplicationManifest` | `app.manifest` | `requireAdministrator` + PerMonitorV2 DPI |
| `Version` | `0.2.0` | bump it for every build you hand out; the installer uses it to offer an update |

`build.ps1` runs the same publish command and prints the size. It finds `dotnet` on PATH, or falls back to a per-user SDK in `%LOCALAPPDATA%\Microsoft\dotnet`.

For a quick compile check without publishing: `dotnet build src\DashStats -c Debug`.

## How PresentMon and PawnIO get bundled

Both are **committed in `vendor/`** and **embedded into DashStats.exe as resources** at build time (see the `<EmbeddedResource>` items in the csproj). Nothing is downloaded at build time or at run time.

| File in `vendor/` | Source | Size | Used for |
|---|---|---|---|
| `PresentMon.exe` | Intel PresentMon **v2.6.0**, console build `PresentMon-2.6.0-x64.exe` from https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0 (Authenticode-signed by Intel Corporation) | ~0.98 MB | FPS / frame times via ETW |
| `PawnIO_setup.exe` | PawnIO **2.2.0** installer from https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0 (Authenticode-signed by namazso.eu) | ~3.4 MB | the kernel driver LibreHardwareMonitor needs for CPU temperature and power |

At run time, `Setup.Extract()` writes an embedded file to `C:\Program Files\DashStats\bin\` (admin-only) right before each launch, unless the copy there already has the same SHA-256 as the bundled one, then:

- **PresentMon** is started by `FpsCollector` as a child process:
  `PresentMon.exe --output_stdout --no_console_stats --stop_existing_session --session_name DashStatsPM --no_track_gpu --no_track_input --exclude dwm.exe --exclude DashStats.exe`
  DashStats parses the CSV from stdout. PresentMon is put in a Windows job object, so it dies if DashStats crashes. On exit, DashStats kills it and runs `--terminate_existing_session` to remove the ETW session.
- **PawnIO** is installed only during first-run setup, and only if the `PawnIO` service is missing (`HKLM\SYSTEM\CurrentControlSet\Services\PawnIO`), with `PawnIO_setup.exe -install -silent`. Without it, everything still works except CPU temperature and CPU power ("needs PawnIO").

### Updating them

1. Download the new release, verify the signature (right-click → Properties → Digital Signatures, or `Get-AuthenticodeSignature`).
2. Replace the file in `vendor/` (keep the file name).
3. Update the version in this file and in `THIRD-PARTY-NOTICES.md`.
4. Rebuild and bump `<Version>`.

## Dev loop

The installed copy lives in `C:\Program Files\DashStats\` and is started by a scheduled task named `DashStats` (run with highest privileges at sign-in). `dev.ps1` rebuilds, ends the task, copies the new exe over the installed one and starts the task again. Run it from an **admin terminal**: Program Files is admin-only on purpose, because the task starts the exe as admin. It needs DashStats to have been installed once via the first-run "Yes, set it up" path.

## Troubleshooting a build on a new PC

- **Smart App Control** blocks unsigned builds outright (Code Integrity event 3077/3118). Turn it off, or code-sign the exe.
- **"InvariantGlobalization" must stay off.** WPF crashes at first text render with it on.
- Settings and log are in `%APPDATA%\DashStats\` (`settings.json`, `log.txt`). Create an empty `debug.flag` there and every value is written to `snapshot.txt` once a second.
