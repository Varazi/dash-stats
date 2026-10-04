# Third-party notices

DashStats includes or redistributes the following third-party software. Each remains under its own license. Full license texts are at the linked sources.

## Bundled executables

These ship inside `DashStats.exe` (from `vendor/`) and are extracted to `C:\Program Files\DashStats\bin\` at run time.

### PresentMon

- Version: 2.6.0 (console application)
- Copyright (C) 2017-2024 Intel Corporation
- License: **MIT**
- Source: https://github.com/GameTechDev/PresentMon
- License text: https://github.com/GameTechDev/PresentMon/blob/main/LICENSE.txt

### PawnIO (installer, driver and modules)

- Version: 2.2.0 (PawnIO.Setup)
- Author: namazso
- PawnIO driver: **GPL-2.0-or-later**, with exceptions allowing use from independent programs that talk to it only through its device IO control interface (as DashStats does, via LibreHardwareMonitor). https://github.com/namazso/PawnIO
- PawnIO modules: **LGPL-2.1**. https://github.com/namazso/PawnIO.Modules
- Installer releases: https://github.com/namazso/PawnIO.Setup
- Website: https://pawnio.eu

DashStats redistributes the unmodified, signed PawnIO installer. Source code for PawnIO is available at the links above.

## Libraries (NuGet)

| Package | Version | License | Source |
|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | **MPL-2.0** | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | https://github.com/Blacktempel/BlackSharp |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| HidSharp | 2.6.4 | Apache-2.0 | https://software.seekye.com/hidsharp |
| Mono.Posix.NETStandard | 1.0.0 | MIT | https://github.com/mono/mono |
| System.Management | 10.0.2 | MIT | https://github.com/dotnet/runtime |
| System.IO.Ports | 10.0.3 | MIT | https://github.com/dotnet/runtime |
| System.IO.FileSystem.AccessControl | 5.0.0 | MIT | https://github.com/dotnet/runtime |

LibreHardwareMonitorLib pulls in the others as dependencies. The MPL-2.0 libraries are used unmodified; their source is available at the links above.

## Runtime

DashStats is published self-contained, so the **.NET 10 runtime, WPF and Windows Forms** (MIT, https://github.com/dotnet/runtime, https://github.com/dotnet/wpf, https://github.com/dotnet/winforms) are included in `DashStats.exe`.
