@echo off
setlocal
title DMAP API (local test) - Leave This Window Open
cd /d "%~dp0"
rem For testing on this PC only (the Android emulator reaches it as 10.0.2.2):
rem listens on this computer alone, with the local test key "local-dev-key".
set "ASPNETCORE_ENVIRONMENT=Development"
dotnet run --project "%~dp0DmapApi\DmapApi.csproj" -- --urls http://127.0.0.1:5080
echo.
echo The DMAP API has stopped.
pause
