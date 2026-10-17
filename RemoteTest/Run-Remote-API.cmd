@echo off
setlocal
title DMAP API - Leave This Window Open
for %%I in ("%~dp0..") do set "REPO_ROOT=%%~fI"
cd /d "%REPO_ROOT%"

rem The access key made by Prepare Remote PC Test.cmd (kept on this PC only).
set "KEY_FILE=%LOCALAPPDATA%\DMAP Guide Server\remote-access-key.txt"
if not exist "%KEY_FILE%" (
    echo The access key has not been made yet. Double-click Prepare Remote PC Test.cmd first.
    pause
    exit /b 1
)
set /p ACCESS_KEY=<"%KEY_FILE%"

rem Production: only the editor key above is accepted, and nobody can choose their own role.
set "ASPNETCORE_ENVIRONMENT=Production"
set "Dmap__ApiKeys__0__Key=%ACCESS_KEY%"
set "Dmap__ApiKeys__0__Role=editor"
dotnet run --project "%REPO_ROOT%\DmapApi\DmapApi.csproj" -- --urls http://127.0.0.1:5080
echo.
echo The DMAP API has stopped.
pause
