# DashStats

A small desktop widget for PC health: CPU/GPU temps, clocks, load and power, RAM, disks, battery, FPS for the
app in front, and network health (speed, ping, jitter, packet loss, retransmits, hiccups).

**Website:** https://varazi.github.io/dash-stats/ · **Download:** [latest release](https://github.com/Varazi/dash-stats/releases/latest) · **Support it:** [buy me a coffee on Ko-fi](https://ko-fi.com/varazi)

DashStats is free and open source (MIT). If it's useful to you, a tip helps keep it updated: one-time, any amount,
or a small monthly contribution. It's entirely optional; there are no paid features.

## Install

Download `DashStats.exe` from the [latest release](https://github.com/Varazi/dash-stats/releases/latest) and
double-click it. Nothing else is needed. If Windows says "Windows protected your PC", click **More info → Run
anyway** (the build isn't code-signed yet). Click **Yes** at the admin prompt and **Yes** at setup. Setup installs
the PawnIO sensor driver, copies DashStats to `C:\Program Files\DashStats`, starts it at sign-in and adds
a Start menu shortcut.

To update later, run the newer `DashStats.exe` from anywhere and it offers to replace the installed copy.
Exit the running one from the tray icon first.

## Controls

| Action | How |
|---|---|
| Choose what to monitor and the look | **EDIT** in the widget header, or tray → *Change what I monitor…* |
| Pin on top of everything, click-through | `Ctrl+Alt+M` or the **PIN** button. Unpin with `Ctrl+Alt+M` or tray → **Unpin widget** |
| Full / lite view | `Ctrl+Alt+L` or the **LITE** button |
| Hide / show | `Ctrl+Alt+H` or left-click the tray icon |
| Move | Drag it. It snaps to the nearest corner. |

## Build

- `.\build.ps1` builds `dist\DashStats.exe`, a self-contained single file.
- `.\dev.ps1` (from an admin terminal) rebuilds and restarts the installed copy.

Settings and log live in `%APPDATA%\DashStats`. For troubleshooting, create `debug.flag` in that folder and
every value is written to `snapshot.txt` each second.

## Ship a new version

1. Bump `<Version>` in `src/DashStats/DashStats.csproj` and commit.
2. Tag and push: `git tag v0.3.0` then `git push && git push --tags` (the tag must match the version).
3. The [Release workflow](.github/workflows/release.yml) builds the exe on GitHub and attaches it to a new
   release. `releases/latest` moves to it automatically, so the website's download button needs no change.
4. Installed copies (0.3.0 and later) notice the new release within a few hours and offer to update.
   This needs the repo (or at least its releases) to be public; while it's private the check quietly fails.

To do it by hand instead: `.\build.ps1`, then `gh release create v0.3.0 dist\DashStats.exe --generate-notes`.

## Website

`docs/index.html` is the one-page site, served by GitHub Pages: one self-contained HTML file, no build step. The
download, GitHub and tip (Stripe Payment Link) URLs are set once in the `DASHSTATS_LINKS` block near the top.
The widgets on the page are drawn live in the browser with example readings, matching the app's looks.
The background is a rolling wave of dots joined into a faint wireframe mesh, with soft smoke in the bottom corners.
Headings use Sora. An alternative aurora background
is kept in `design/website-aurora.html`.

## Notes

- Windows **Smart App Control** blocks the unsigned exe. Turn it off, or code-sign the build.
- Sensors come from LibreHardwareMonitor (plus the PawnIO driver), FPS from Intel PresentMon. Both are
  bundled inside the exe.
- Exclusive-fullscreen games draw over every window. Use borderless/windowed fullscreen to see the overlay.

## More

- [BUILDING.md](BUILDING.md): toolchain, the exact publish command, how PresentMon and PawnIO are bundled.
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md): licenses of bundled components.
- [LICENSE](LICENSE): MIT.
