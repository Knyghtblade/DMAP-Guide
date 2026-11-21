$ErrorActionPreference = 'Stop'

# Builds DMAP Guide as a single .exe that needs nothing installed, into:
#
#   publish\DMAP Guide\
#       DMAP Guide.exe      the app: copy this (and READ ME.txt) over the ones in the shared folder
#       READ ME.txt         what to tell colleagues
#
# It never makes a "guide" folder here. The shared guide lives only in the shared OneDrive folder;
# on 20 Nov 2026 an old guide folder left beside a new build was copied over the shared one by mistake.
#
# It also refuses to build a version that isn't newer than the newest one that has opened the shared
# guide (its app-version.txt), so a PC that hasn't picked up the latest changes can't replace a newer app.

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'DmapGuide\DmapGuide.csproj'
$out = Join-Path $repoRoot 'publish\DMAP Guide'

# Where the shared folder is on this PC (the inner folder holding the app and the guide).
$shared = Join-Path $env:USERPROFILE 'OneDrive - Hasbro Inc\DMAP Guide\Dungeon Masters Production Guide'

function Get-VersionValue([string]$text) {
    if ($text -match '(\d+)\.(\d+)\.(\d+)') { return [version]"$($Matches[1]).$($Matches[2]).$($Matches[3])" }
    return $null
}

try {
    # Every shipped build gets a new version number (the last part goes up by one), so the
    # title bar and Settings show which build someone is on.
    $csproj = Get-Content $project -Raw
    if ($csproj -notmatch '<Version>(\d+)\.(\d+)\.(\d+)</Version>') { throw 'DmapGuide.csproj has no <Version>major.minor.build</Version> line.' }
    $version = "$($Matches[1]).$($Matches[2]).$([int]$Matches[3] + 1)"

    # Not older than what the shared guide has already seen.
    $seenFile = Join-Path $shared 'guide\app-version.txt'
    if (Test-Path $seenFile) {
        $seen = Get-VersionValue (Get-Content $seenFile -Raw)
        if ($seen -and [version]$version -le $seen) {
            throw "Stopped: this would build DMAP Guide $version, but $seen has already been used with the shared guide.`n" +
                  "This PC is missing newer changes made on another PC. Ask Claude to bring them in from GitHub first."
        }
    }

    $csproj = $csproj -replace '<Version>\d+\.\d+\.\d+</Version>', "<Version>$version</Version>"
    Set-Content $project $csproj -NoNewline -Encoding UTF8

    # A guide folder must never sit beside a new build, where it could be copied over the shared one.
    $strayGuide = Join-Path $out 'guide'
    if (Test-Path $strayGuide) { Remove-Item $strayGuide -Recurse -Force }

    Write-Host "Building DMAP Guide $version as one file. This takes a minute or two..."
    & dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $out -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'The build failed. Read the message above.' }

    # Only the app and READ ME are left here.
    Get-ChildItem $out | Where-Object { $_.Name -notin @('DMAP Guide.exe', 'READ ME.txt') } | Remove-Item -Recurse -Force
    Copy-Item (Join-Path $PSScriptRoot 'READ ME.txt') $out -Force

    $size = [math]::Round((Get-Item (Join-Path $out 'DMAP Guide.exe')).Length / 1MB)
    Write-Host ''
    Write-Host "Done. DMAP Guide $version is built ($size MB), in:" -ForegroundColor Green
    Write-Host "  $out"
    Write-Host ''
    Write-Host 'Next:'
    Write-Host '  1. Close DMAP Guide if it is open.'
    Write-Host '  2. Copy "DMAP Guide.exe" and "READ ME.txt" from the folder above into the shared folder,'
    Write-Host "     replacing the ones there:  $shared"
    Write-Host '  3. Copy only those two files. Never copy a "guide" folder into the shared folder.'
    Write-Host '  4. Open DMAP Guide from the shared folder and check the title bar says' $version
    Write-Host ''
    Start-Process explorer.exe $out
}
catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
