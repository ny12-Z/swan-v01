@echo off
set "DATA=%~dp0Builds\SwanV01_Data\StreamingAssets\Data"
if not exist "%DATA%" (
  echo Data folder not found: %DATA%
  pause
  exit /b 1
)
start "" explorer "%DATA%"
