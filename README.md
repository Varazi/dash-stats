# DashStats

A small desktop widget for PC health: CPU/GPU temps, clocks, load and power, RAM, disks, battery, FPS for the
app in front, and network health (speed, ping, jitter, packet loss, retransmits, hiccups).

## Install on another PC

Copy `dist\DashStats.exe` over and double-click it. Nothing else is needed. Click **Yes** at the admin prompt
and **Yes** at setup. Setup installs the PawnIO sensor driver, copies DashStats to
`%LOCALAPPDATA%\Programs\DashStats`, starts it at sign-in and adds a Start menu shortcut.

To update later, run the newer `DashStats.exe` from anywhere and it offers to replace the installed copy.
Exit the running one from the tray icon first.

## Controls

| Action | How |
|---|---|
| Choose what to monitor and the look | **EDIT** in the widget header, or tray → *Change what I monitor…* |
| Every reading vs. your picks | **ALL** / **MINE** in the header |
| Pin on top of everything, click-through | `Ctrl+Alt+M` or the **PIN ON TOP** button |
| Full / lite view | `Ctrl+Alt+L` or the **LITE** button |
| Hide / show | `Ctrl+Alt+H` or left-click the tray icon |
| Move | Drag it. It snaps to the nearest corner. |
| Prune a row | Right-click it → **Hide** (the tray menu brings hidden rows back) |

## Build

- `.\build.ps1` builds `dist\DashStats.exe`, a self-contained single file.
- `.\dev.ps1` rebuilds and restarts the installed copy without a UAC prompt.

Settings and log live in `%APPDATA%\DashStats`. For troubleshooting, create `debug.flag` in that folder and
every value is written to `snapshot.txt` each second.

## Notes

- Windows **Smart App Control** blocks the unsigned exe. Turn it off, or code-sign the build.
- Sensors come from LibreHardwareMonitor (plus the PawnIO driver), FPS from Intel PresentMon. Both are
  bundled inside the exe.
- Exclusive-fullscreen games draw over every window. Use borderless/windowed fullscreen to see the overlay.

## More

- [BUILDING.md](BUILDING.md): toolchain, the exact publish command, how PresentMon and PawnIO are bundled.
- [docs/NOTES.md](docs/NOTES.md): decisions, to-do list, known bugs, tested hardware, measured resource use.
- [docs/PROJECT-BRIEF.md](docs/PROJECT-BRIEF.md): write-up for the website.
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md): licenses of bundled components.
