@echo off
setlocal
title DMAP API - Leave This Window Open
for %%I in ("%~dp0..") do set "REPO_ROOT=%%~fI"
cd /d "%REPO_ROOT%"
set "ASPNETCORE_ENVIRONMENT=Development"
set "Dmap__ApiKey=local-dev-key"
dotnet run --project "%REPO_ROOT%\DmapApi\DmapApi.csproj" -- --urls http://127.0.0.1:5080
echo.
echo The DMAP API has stopped.
pause