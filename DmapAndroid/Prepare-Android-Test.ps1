$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = '.\DmapAndroid\DmapAndroid.csproj'
$outputFolder = Join-Path $repoRoot 'AndroidPhoneTest'
Set-Location $repoRoot

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
        if (-not (Test-Path $javac)) {
            continue
        }

        $version = & $javac -version 2>&1 | Out-String
        if ($version -match 'javac 21(?:\.|\s|$)') {
            return $candidate
        }
    }

    return $null
}

try {
    $workloads = (& dotnet workload list 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or $workloads -notmatch '(?m)^\s*maui-android\s') {
        Write-Host 'Android build support is not installed. Installing it now; this may take several minutes.' -ForegroundColor Cyan
        & dotnet workload install maui-android
        if ($LASTEXITCODE -ne 0) {
            throw 'Android build support could not be installed. If Windows asks for permission, approve it. If this is a company PC, ask IT to install the .NET MAUI Android workload.'
        }
    }

    $sdkCandidates = @($env:ANDROID_SDK_ROOT, $env:ANDROID_HOME, (Join-Path $env:LOCALAPPDATA 'Android\Sdk')) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } |
        Select-Object -Unique
    $sdkDirectory = $sdkCandidates | Select-Object -First 1
    if (-not $sdkDirectory -or -not (Test-Path (Join-Path $sdkDirectory 'platforms'))) {
        throw 'The Android SDK is not installed yet. Install Android Studio from https://developer.android.com/studio. Open it once, choose the Standard setup, and accept the suggested Android SDK installation. Then double-click Prepare Android Phone Test.cmd again.'
    }

    $env:ANDROID_SDK_ROOT = $sdkDirectory
    $env:ANDROID_HOME = $sdkDirectory

    $javaDirectory = Find-Jdk21
    if (-not $javaDirectory) {
        $answer = Read-Host 'This Android build needs Java 21. Install the official Microsoft Java 21 now? Type Y to continue'
        if ($answer -notmatch '^[Yy]') {
            throw 'Java 21 is required. Install Microsoft OpenJDK 21, then double-click Prepare Android Phone Test.cmd again.'
        }

        $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
        if (-not $winget) {
            throw 'Windows Package Manager was not found. Install Microsoft OpenJDK 21 from https://learn.microsoft.com/java/openjdk/download, then run this file again.'
        }

        Write-Host 'Installing Microsoft Java 21...' -ForegroundColor Cyan
        & $winget.Source install --id Microsoft.OpenJDK.21 --exact --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) {
            throw 'Java 21 installation did not finish. Install Microsoft OpenJDK 21 from https://learn.microsoft.com/java/openjdk/download, then run this file again.'
        }

        $javaDirectory = Find-Jdk21
        if (-not $javaDirectory) {
            throw 'Java 21 installed, but Windows has not registered it yet. Restart this computer, then double-click Prepare Android Phone Test.cmd again.'
        }
    }

    $env:JAVA_HOME = $javaDirectory
    $env:JavaSdkDirectory = $javaDirectory

    $androidJar = Join-Path $sdkDirectory 'platforms\android-36\android.jar'
    if (-not (Test-Path $androidJar)) {
        $licenseAnswer = Read-Host 'Android requires acceptance of Google Android SDK license terms to continue. Type Y to accept and install the required platform'
        if ($licenseAnswer -notmatch '^[Yy]') {
            throw 'Android SDK license terms were not accepted, so the phone app was not built.'
        }

        Write-Host 'Installing the Android platform needed by this app...' -ForegroundColor Cyan
        & dotnet build $project -t:InstallAndroidDependencies -f net10.0-android -p:AcceptAndroidSDKLicenses=true
        if ($LASTEXITCODE -ne 0) {
            throw 'Android platform setup did not finish. Send the full error text above for help.'
        }
    }

    if (-not (Test-Path $androidJar)) {
        throw 'Android platform 36 is still missing. Open Android Studio, install Android SDK Platform 36 in SDK Manager, then run this file again.'
    }

    New-Item -ItemType Directory -Path $outputFolder -Force | Out-Null
    Get-ChildItem -Path $outputFolder -Filter '*.apk' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
    Write-Host 'Building the Android app. This can take several minutes the first time.' -ForegroundColor Cyan
    & dotnet publish $project -f net10.0-android -c Release -p:AndroidPackageFormat=apk -p:EmbedAssembliesIntoApk=true -o .\AndroidPhoneTest
    if ($LASTEXITCODE -ne 0) {
        throw 'The Android app could not be built. Send the error text above for help.'
    }

    $apk = Get-ChildItem -Path $outputFolder -Filter '*-Signed.apk' -Recurse | Select-Object -First 1
    if ($null -eq $apk) {
        $apk = Get-ChildItem -Path $outputFolder -Filter '*.apk' -Recurse | Select-Object -First 1
    }
    if ($null -eq $apk) {
        throw 'The build reported success but no APK was found in AndroidPhoneTest. Send the build output above for help.'
    }

    Write-Host ''
    Write-Host 'READY' -ForegroundColor Green
    Write-Host "Phone app file: $($apk.FullName)"
    Write-Host 'Copy this APK to your Android phone, open it, and approve the install prompt.'
    Invoke-Item $outputFolder
}
catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}