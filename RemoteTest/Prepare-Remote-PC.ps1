$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repoRoot 'DmapApi\DmapApi.csproj'
$desktopProject = Join-Path $repoRoot 'DmapGuide\DmapGuide.csproj'
$apiLauncher = Join-Path $PSScriptRoot 'Run-Remote-API.cmd'

try {
    $tailscaleCommand = Get-Command tailscale.exe -ErrorAction SilentlyContinue
    $tailscalePath = if ($tailscaleCommand) { $tailscaleCommand.Source } else { Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe' }
    if (-not (Test-Path $tailscalePath)) {
        throw 'Tailscale is not installed. Install it and sign in on this PC, then double-click this file again.'
    }

    $tailnetStatusJson = & $tailscalePath status --json
    if ($LASTEXITCODE -ne 0) {
        throw 'Tailscale is not connected. Open Tailscale, sign in, and try again.'
    }
    $tailnetStatus = $tailnetStatusJson | ConvertFrom-Json
    $hostName = $tailnetStatus.Self.DNSName.TrimEnd('.')
    if ([string]::IsNullOrWhiteSpace($hostName)) {
        throw 'Tailscale is not connected to a tailnet. Open Tailscale, sign in, and try again.'
    }

    $listener = Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener) {
        throw 'The API is already running on port 5080. Find its terminal and press Ctrl+C, then double-click this file again.'
    }

    if (-not (Test-Path $apiProject) -or -not (Test-Path $desktopProject)) {
        throw 'The project files were not found. Double-click this file from inside the DMAP Guide project folder.'
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $packageRoot = Join-Path $repoRoot "RemoteTestPackage-$stamp"
    $publishFolder = Join-Path $packageRoot 'DMAP Guide'
    New-Item -ItemType Directory -Path $publishFolder -Force | Out-Null

    Write-Host 'Preparing the DMAP Guide app for the other PC. This can take a few minutes.' -ForegroundColor Cyan
    & dotnet publish $desktopProject -c Release -r win-x64 --self-contained true -o $publishFolder
    if ($LASTEXITCODE -ne 0) {
        throw 'The desktop app could not be prepared. Please send the text shown above for help.'
    }

    # A private access key for this test, made once and kept on this PC.
    $keyFolder = Join-Path $env:LOCALAPPDATA 'DMAP Guide Server'
    $keyFile = Join-Path $keyFolder 'remote-access-key.txt'
    if (-not (Test-Path $keyFile)) {
        New-Item -ItemType Directory -Path $keyFolder -Force | Out-Null
        $bytes = New-Object byte[] 24
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        $newKey = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        [System.IO.File]::WriteAllText($keyFile, $newKey, [System.Text.UTF8Encoding]::new($false))
    }
    $accessKey = ([System.IO.File]::ReadAllText($keyFile)).Trim()

    $apiBaseUrl = "https://$hostName/"
    $settings = [ordered]@{
        UseServer = $true
        ApiBaseUrl = $apiBaseUrl
        ApiKey = $accessKey
    }
    $settingsPath = Join-Path $publishFolder 'settings.json'
    $settingsJson = $settings | ConvertTo-Json
    [System.IO.File]::WriteAllText($settingsPath, $settingsJson, [System.Text.UTF8Encoding]::new($false))

    $connectScript = @'
@echo off
setlocal
set "SETTINGS_FOLDER=%APPDATA%\DMAP Guide"
if not exist "%SETTINGS_FOLDER%" mkdir "%SETTINGS_FOLDER%"
copy /Y "%~dp0settings.json" "%SETTINGS_FOLDER%\settings.json" >nul
if errorlevel 1 (
    echo Could not configure DMAP Guide. Please contact the person who prepared this package.
    pause
    exit /b 1
)
if not exist "%~dp0DMAP Guide.exe" (
    echo DMAP Guide.exe was not found in this folder.
    pause
    exit /b 1
)
start "" "%~dp0DMAP Guide.exe"
'@
    [System.IO.File]::WriteAllText(
        (Join-Path $publishFolder 'Connect to DMAP Guide.cmd'),
        $connectScript,
        [System.Text.Encoding]::ASCII)

    $instructions = @"
DMAP GUIDE REMOTE TEST

1. Install Tailscale on this PC and sign in to the same account used on the PC hosting the API.
    Download it from https://tailscale.com/download/windows. On a company PC, get permission before installing it.
2. Make sure Tailscale says Connected.
3. Extract the DMAP Guide folder from this package.
4. Close DMAP Guide if it is already open.
5. Double-click Connect to DMAP Guide.cmd.
6. When the book window appears, click its cover to open it.

The API-hosting PC must stay on, connected to Tailscale, and have its API window open.
This is a private proof-of-concept test. The package contains the guide's access key:
only share it with a trusted PC on your tailnet. To shut everyone out, delete
%LOCALAPPDATA%\DMAP Guide Server\remote-access-key.txt on the host PC and prepare a new package.
"@
    [System.IO.File]::WriteAllText(
        (Join-Path $publishFolder 'READ THIS FIRST.txt'),
        $instructions,
        [System.Text.Encoding]::UTF8)

    $zipPath = "$packageRoot.zip"
    Compress-Archive -Path $publishFolder -DestinationPath $zipPath -Force

    Write-Host 'Setting up the private connection to this PC...' -ForegroundColor Cyan
    & $tailscalePath serve --bg http://127.0.0.1:5080
    if ($LASTEXITCODE -ne 0) {
        throw 'Tailscale could not set up the private connection. Leave the package as-is and send the text shown above for help.'
    }

    Start-Process -FilePath $env:ComSpec -ArgumentList @('/k', "`"$apiLauncher`"") -WorkingDirectory $repoRoot

    Write-Host ''
    Write-Host 'READY' -ForegroundColor Green
    Write-Host "The API address is: $apiBaseUrl"
    Write-Host "The access key is:  $accessKey"
    Write-Host '(Enter both in Settings on a phone, or on this PC''s DMAP Guide, to use the shared guide.)'
    Write-Host 'A new window named DMAP API should open. Leave that window open.'
    Write-Host "Send this package to the other PC: $zipPath"
    Write-Host 'On the other PC, install Tailscale, sign in to the same account, unzip the package, and double-click Connect to DMAP Guide.cmd.'
}
catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}