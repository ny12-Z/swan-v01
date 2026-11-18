# Run the EditMode test suite in batchmode and print a one-screen summary.
# Usage:  powershell -ExecutionPolicy Bypass -File tools\run-tests.ps1
# Exit code: 0 = all passed, 1 = failures or no results.
# NOTE: keep this file ASCII-only (this machine decodes UTF-8 sources as the legacy codepage).
param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe",
    [string]$ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [int]$TimeoutSeconds = 900
)

$results = Join-Path $ProjectPath "Logs\editmode-results.xml"
$log = Join-Path $ProjectPath "Logs\tests.log"
New-Item -ItemType Directory -Force -Path (Join-Path $ProjectPath "Logs") | Out-Null
Remove-Item $results, $log -ErrorAction SilentlyContinue

# -runTests must NOT be combined with -quit, otherwise Unity quits before running the tests.
# Unity is started detached through WMI and the results file is polled: PowerShell does not
# reliably wait for this GUI process, and Start-Process hangs in some hosts.
$arguments = '-batchmode -projectPath "{0}" -runTests -testPlatform EditMode -testResults "{1}" -logFile "{2}"' -f `
    $ProjectPath, $results, $log
$commandLine = '"{0}" {1}' -f $UnityPath, $arguments
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine = $commandLine
    CurrentDirectory = $ProjectPath
} | Out-Null

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not (Test-Path $results) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 700 }
Start-Sleep -Milliseconds 500

if (-not (Test-Path $results)) {
    Write-Host "No test results were produced. Tail of $log :" -ForegroundColor Red
    if (Test-Path $log) { Get-Content $log -Tail 25 | ForEach-Object { Write-Host $_ } }
    exit 1
}

$raw = [System.IO.File]::ReadAllText($results, [System.Text.Encoding]::UTF8)
$run = [regex]::Match($raw, '<test-run[^>]*>').Value
$fields = @{}
foreach ($m in [regex]::Matches($run, '(\w+)="([^"]*)"')) { $fields[$m.Groups[1].Value] = $m.Groups[2].Value }

Write-Host ("Tests: total={0} passed={1} failed={2} skipped={3}" -f $fields['total'], $fields['passed'], $fields['failed'], $fields['skipped'])
foreach ($m in [regex]::Matches($raw, '<test-case[^>]*result="Failed"[^>]*>')) {
    $name = [regex]::Match($m.Value, 'fullname="([^"]*)"').Groups[1].Value
    Write-Host "FAILED: $name" -ForegroundColor Red
}

if ($fields['result'] -eq 'Passed') { Write-Host "All green." -ForegroundColor Green; exit 0 }
exit 1
