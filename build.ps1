# Builds dist\DashStats.exe: one self-contained file to copy to any Windows 10/11 x64 PC.
$ErrorActionPreference = 'Stop'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw ".NET SDK not found. Install it with: winget install Microsoft.DotNet.SDK.10" }

$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
$env:DOTNET_NOLOGO = 1

& $dotnet publish "$PSScriptRoot\src\DashStats\DashStats.csproj" -c Release -o "$PSScriptRoot\dist"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$exe = Get-Item "$PSScriptRoot\dist\DashStats.exe"
"Built {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)
