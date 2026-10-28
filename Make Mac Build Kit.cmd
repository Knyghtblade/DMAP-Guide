@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0SharedApp\Make-Mac-Kit.ps1"
if errorlevel 1 (
    echo.
    echo The Mac build kit was not made. Read the message above.
)
echo.
pause
