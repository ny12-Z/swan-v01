@echo off
cd /d "%~dp0Builds"
if not exist "SwanV01.exe" (
  echo Builds\SwanV01.exe not found. Build it in Unity: SWAN -^> 2.
  pause
  exit /b 1
)
start "" "SwanV01.exe" -force-d3d11
