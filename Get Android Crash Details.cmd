@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0DmapAndroid\Get-Android-Crash-Log.ps1"
echo.
pause
