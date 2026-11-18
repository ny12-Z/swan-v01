# Build the Windows player into Builds/SwanV01.exe.
# Usage:  powershell -ExecutionPolicy Bypass -File tools\build.ps1
# Exit code: 0 = build succeeded, 1 = failed.
# NOTE: keep this file ASCII-only (this machine decodes UTF-8 sources as the legacy codepage).
param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe",
    [string]$ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [int]$TimeoutSeconds = 1200
)

$log = Join-Path $ProjectPath "Logs\build.log"
$exe = Join-Path $ProjectPath "Builds\SwanV01.exe"
New-Item -ItemType Directory -Force -Path (Join-Path $ProjectPath "Logs") | Out-Null
Remove-Item $log -ErrorAction SilentlyContinue

# Detached launch through WMI + poll for the "return code N" line Unity writes at the end.
$arguments = '-batchmode -quit -projectPath "{0}" -executeMethod Swan.EditorTools.SceneBuilder.BuildWindows -logFile "{1}"' -f `
    $ProjectPath, $log
$commandLine = '"{0}" {1}' -f $UnityPath, $arguments
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine = $commandLine
    CurrentDirectory = $ProjectPath
} | Out-Null

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$finished = $false
while (-not $finished -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 1000
    if (Test-Path $log) {
        $tail = Get-Content $log -Tail 5
        $finished = ($tail -join "`n") -match "return code"
    }
}
Start-Sleep -Milliseconds 500

if (Test-Path $log) { Get-Content $log -Tail 15 | ForEach-Object { Write-Host $_ } }

# The player exe is not always rewritten on an incremental build (only the data folder changes),
# so success is judged from Unity's own ASCII result line plus the artifact existing.
$success = (Test-Path $log) -and (Select-String -Path $log -Pattern 'Build Finished, Result: Success' -Quiet)
if ($finished -and $success -and (Test-Path $exe)) {
    Write-Host "Built: $exe" -ForegroundColor Green
    exit 0
}

Write-Host "Build FAILED. See $log" -ForegroundColor Red
exit 1
