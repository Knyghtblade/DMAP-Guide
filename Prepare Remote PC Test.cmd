@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0RemoteTest\Prepare-Remote-PC.ps1"
if errorlevel 1 (
    echo.
    echo Setup did not finish. Read the message above.
)
echo.
pause