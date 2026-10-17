@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0DmapAndroid\Run-Android-Emulator.ps1"
echo.
pause
