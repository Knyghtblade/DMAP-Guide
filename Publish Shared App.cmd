@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0SharedApp\Publish-Shared-App.ps1"
if errorlevel 1 (
    echo.
    echo The shared folder was not built. Read the message above.
)
echo.
pause
