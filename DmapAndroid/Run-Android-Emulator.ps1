$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = '.\DmapAndroid\DmapAndroid.csproj'

function Find-Jdk21 {
    $candidatePaths = @(
        $env:JAVA_HOME,
        (Join-Path $env:ProgramFiles 'Android\Android Studio\jbr'),
        (Join-Path $env:ProgramFiles 'Android\Android Studio\jre')
    )
    $searchRoots = @(
        (Join-Path $env:ProgramFiles 'Java'),
        (Join-Path $env:ProgramFiles 'Microsoft'),
        (Join-Path $env:ProgramFiles 'Eclipse Adoptium'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Microsoft'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Eclipse Adoptium')
    )
    foreach ($root in $searchRoots) {
        if (Test-Path $root) {
            $candidatePaths += Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty FullName
        }
    }
    foreach ($candidate in ($candidatePaths | Where-Object { $_ } | Select-Object -Unique)) {
        $javac = Join-Path $candidate 'bin\javac.exe'
        if ((Test-Path $javac) -and ((& $javac -version 2>&1 | Out-String) -match 'javac 21(?:\.|\s|$)')) {
            return $candidate
        }
    }
    return $null
}

try {
    Push-Location $repoRoot

    $sdkCandidates = @($env:ANDROID_SDK_ROOT, $env:ANDROID_HOME, (Join-Path $env:LOCALAPPDATA 'Android\Sdk')) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) }
    $sdkDirectory = $sdkCandidates | Select-Object -First 1
    if (-not $sdkDirectory -or -not (Test-Path (Join-Path $sdkDirectory 'platform-tools\adb.exe'))) {
        throw 'Android build tools are missing. Double-click Prepare Android Phone Test.cmd once to install them, then try again.'
    }

    $javaDirectory = Find-Jdk21
    if (-not $javaDirectory) {
        throw 'Java 21 is missing. Double-click Prepare Android Phone Test.cmd and follow its Java setup prompt, then try again.'
    }

    $env:ANDROID_SDK_ROOT = $sdkDirectory
    $env:ANDROID_HOME = $sdkDirectory
    $env:JAVA_HOME = $javaDirectory
    $env:JavaSdkDirectory = $javaDirectory

    $adb = Join-Path $sdkDirectory 'platform-tools\adb.exe'
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $devices = & $adb devices 2>&1 | ForEach-Object { $_.ToString() }
        $adbExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($adbExitCode -ne 0) {
        throw 'Android tools could not check for a running virtual phone. Close and reopen Android Studio, then try again.'
    }
    $emulator = $devices | Where-Object { [regex]::IsMatch($_, '^emulator-\d+\s+device$') } | Select-Object -First 1
    if (-not $emulator) {
        throw 'No virtual phone is running yet. Open Android Studio, choose More Actions > Virtual Device Manager, and press the Play triangle beside your virtual phone. Wait for it to finish starting, then double-click this file again.'
    }

    $apiListener = Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $apiListener) {
        throw 'The guide API is not running on this computer. Start the DMAP API window first, then double-click this file again.'
    }

    if (-not (Test-Path $project)) {
        throw 'The Android project was not found. Open this tool from inside the DMAP Guide project folder.'
    }

    Write-Host 'Building and opening DMAP Guide in the virtual phone. This can take a few minutes.' -ForegroundColor Cyan
    & dotnet build $project -t:Run -f net10.0-android -c Debug -p:EmbedAssembliesIntoApk=true
    if ($LASTEXITCODE -ne 0) {
        throw 'The emulator build did not finish. Send the full error text above for help.'
    }

    Write-Host 'DMAP Guide should now be open in the virtual phone.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
