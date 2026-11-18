# Launch the built player, let it run a few seconds, screenshot, and quit by itself.
# Usage:  pwsh -File tools/smoke.ps1
# Exit code: 0 = screenshot was produced, 1 = timeout.
# NOTE: keep this file ASCII-only (PowerShell reads it as the legacy codepage).
param(
    [string]$ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [string]$ExePath,
    [double]$Seconds = 6,
    [double]$Speed = 60,
    [string]$ShotPath,
    [int]$Width = 1280,
    [int]$Height = 720,
    [int]$TimeoutSeconds = 120
)

if (-not $ExePath) { $ExePath = Join-Path $ProjectPath "Builds\SwanV01.exe" }
if (-not $ShotPath) { $ShotPath = Join-Path $ProjectPath "Docs\screenshots\10-player-smoke.png" }
$log = Join-Path $ProjectPath "Logs\player.log"

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ShotPath) | Out-Null
Remove-Item $ShotPath -ErrorAction SilentlyContinue
Remove-Item $log -ErrorAction SilentlyContinue

$exe = (Resolve-Path $ExePath).Path
$shot = [System.IO.Path]::GetFullPath($ShotPath)
$commandLine = '"{0}" -swanSmokeSeconds {1} -swanSpeed {2} -swanShot "{3}" -logFile "{4}" -screen-width {5} -screen-height {6} -screen-fullscreen 0' -f `
    $exe, $Seconds, $Speed, $shot, $log, $Width, $Height

# Start-Process hangs in this environment and Start-Job kills child GUI apps on teardown,
# so the player is created through WMI and left detached.
$result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine = $commandLine
    CurrentDirectory = (Split-Path -Parent $exe)
}
Write-Host ("pid={0} cmd={1}" -f $result.ProcessId, $commandLine)

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if (Test-Path $shot) {
        Start-Sleep -Milliseconds 800
        $info = Get-Item $shot
        Write-Host ("Screenshot: {0} ({1} bytes)" -f $info.FullName, $info.Length) -ForegroundColor Green
        if (Test-Path $log) { Get-Content $log -Tail 12 | ForEach-Object { Write-Host $_ } }
        exit 0
    }
    Start-Sleep -Milliseconds 500
}

Write-Host "TIMEOUT: no screenshot. Player log:" -ForegroundColor Red
if (Test-Path $log) { Get-Content $log -Tail 40 | ForEach-Object { Write-Host $_ } }
exit 1
