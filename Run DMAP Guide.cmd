@echo off
setlocal
title DMAP Guide
cd /d "%~dp0"
echo Getting DMAP Guide ready...
dotnet build "DmapGuide\DmapGuide.csproj" -nologo -v q
if errorlevel 1 (
    echo.
    echo DMAP Guide could not be built. Read the message above.
    pause
    exit /b 1
)
start "" "DmapGuide\bin\Debug\net10.0-windows\DMAP Guide.exe"
