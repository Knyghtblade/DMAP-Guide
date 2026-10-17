@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0DmapAndroid\Prepare-Android-Test.ps1"
if errorlevel 1 (
    echo.
    echo The phone package was not created. Read the message above.
)
echo.
pause