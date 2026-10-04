# Rebuild and hot-swap the installed copy (uses the "DashStats" startup task to stop and start it).
# Run it from an ADMIN terminal: the install lives in Program Files, which only admins can write to.
# Needs DashStats installed once via first-run setup ("Yes"), and DashStats started by that task,
# not by double-clicking the exe (the task can't stop a copy it didn't start; exit that one from the tray).
$ErrorActionPreference = 'Stop'
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw "Run dev.ps1 from an administrator terminal (the installed copy is in Program Files)." }
& "$PSScriptRoot\build.ps1"

schtasks /End /TN DashStats | Out-Null
for ($i = 0; $i -lt 30 -and (Get-Process DashStats -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 500 }
if (Get-Process DashStats -ErrorAction SilentlyContinue) {
    throw "A DashStats copy the task didn't start is still running. Exit it from the tray icon and run this again."
}

$target = "$env:ProgramFiles\DashStats\DashStats.exe"
for ($i = 0; ; $i++) {
    try { Copy-Item "$PSScriptRoot\dist\DashStats.exe" $target -Force; break }
    catch { if ($i -ge 40) { throw } ; Start-Sleep -Milliseconds 500 }
}

# Right after /End the task sometimes ignores /Run, so retry until a new process appears.
for ($try = 0; $try -lt 8 -and -not (Get-Process DashStats -ErrorAction SilentlyContinue); $try++) {
    schtasks /Run /TN DashStats | Out-Null
    for ($i = 0; $i -lt 10 -and -not (Get-Process DashStats -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 500 }
}
if (Get-Process DashStats -ErrorAction SilentlyContinue) { "Restarted DashStats" } else { throw "DashStats did not start; see %APPDATA%\DashStats\log.txt" }
