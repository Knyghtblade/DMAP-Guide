$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$reportFolder = Join-Path $repoRoot 'AndroidPhoneTest'

$sdkCandidates = @(
    $env:ANDROID_SDK_ROOT,
    $env:ANDROID_HOME,
    (Join-Path $env:LOCALAPPDATA 'Android\Sdk')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) }

$adb = $null
foreach ($sdk in $sdkCandidates) {
    $candidate = Join-Path $sdk 'platform-tools\adb.exe'
    if (Test-Path $candidate) {
        $adb = $candidate
        break
    }
}

if (-not $adb) {
    Write-Host 'Android debugging tools were not found. Open Android Studio once and finish its setup, then try again.' -ForegroundColor Red
    exit 1
}

function Invoke-Adb {
    param([string[]]$Arguments)

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $adb @Arguments 2>&1 | ForEach-Object { $_.ToString() }
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($exitCode -ne 0) {
        throw "Android debugging tools reported an error: $($output -join [Environment]::NewLine)"
    }

    return $output
}

$devices = Invoke-Adb @('devices')
$connectedDevice = $devices | Where-Object { [regex]::IsMatch($_, '^\S+\s+device$') }
if (-not $connectedDevice) {
    Write-Host 'The phone is not authorized for USB debugging yet.' -ForegroundColor Red
    Write-Host 'On the phone, enable USB debugging and accept the Allow USB debugging prompt. Then run this file again.'
    exit 1
}

Write-Host 'This clears temporary Android diagnostic logs only. It does not change the guide or app.' -ForegroundColor Cyan
Invoke-Adb @('logcat', '-c') | Out-Null
Write-Host 'Now open DMAP Guide on the phone and let it close.'
Read-Host 'When the app has closed, return here and press Enter'

New-Item -ItemType Directory -Path $reportFolder -Force | Out-Null
$reportPath = Join-Path $reportFolder 'DMAP-Android-Crash.txt'
$crashLog = Invoke-Adb @('logcat', '-d', '-b', 'crash', '-v', 'time')
$mainLog = Invoke-Adb @('logcat', '-d', '-b', 'main', '-v', 'time', '-t', '3000')
$markers = @('com.dmapguide.mobile', 'AndroidRuntime', 'FATAL EXCEPTION', 'Fatal signal', 'monodroid', 'Unhandled exception', 'DOTNET')
$appLog = foreach ($line in $mainLog) {
    foreach ($marker in $markers) {
        if ($line.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $line
            break
        }
    }
}

$report = @()
if ($crashLog) {
    $report += '=== ANDROID CRASH BUFFER ==='
    $report += $crashLog
}
if ($appLog) {
    $report += '=== DMAP APP / STARTUP LOG ==='
    $report += $appLog
}
$report | Set-Content -Path $reportPath -Encoding UTF8

if ($report.Count -gt 0) {
    Write-Host 'Crash details saved here:' -ForegroundColor Green
    Write-Host $reportPath
    Write-Host 'Please attach that text file here. It contains crash/startup diagnostics, not the full phone log.'
}
else {
    'No crash or DMAP startup entries were recorded. Confirm the app was opened after the collector started, then run the collector again.' | Set-Content -Path $reportPath -Encoding UTF8
    Write-Host 'No crash signature was found. A report was saved here:' -ForegroundColor Yellow
    Write-Host $reportPath
}
