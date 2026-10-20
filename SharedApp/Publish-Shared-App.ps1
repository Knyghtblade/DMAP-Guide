$ErrorActionPreference = 'Stop'

# Builds DMAP Guide as a single .exe that needs nothing installed, and lays out the
# folder to put on the work OneDrive:
#
#   publish\DMAP Guide\
#       DMAP Guide.exe      the app (everyone double-clicks this)
#       guide\              the guide itself (one small file per page, plus pictures)
#       READ ME.txt         what to tell colleagues
#
# The app always opens the "guide" folder beside it, so nobody needs settings, a server
# or an access key. Who can change the guide is set by the folder's sharing permissions.

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'DmapGuide\DmapGuide.csproj'
$out = Join-Path $repoRoot 'publish\DMAP Guide'
$guide = Join-Path $out 'guide'

try {
    # Every shipped build gets a new version number (the last part goes up by one), so the
    # title bar and Settings show which build someone is on.
    $csproj = Get-Content $project -Raw
    if ($csproj -notmatch '<Version>(\d+)\.(\d+)\.(\d+)</Version>') { throw 'DmapGuide.csproj has no <Version>major.minor.build</Version> line.' }
    $version = "$($Matches[1]).$($Matches[2]).$([int]$Matches[3] + 1)"
    $csproj = $csproj -replace '<Version>\d+\.\d+\.\d+</Version>', "<Version>$version</Version>"
    Set-Content $project $csproj -NoNewline -Encoding UTF8

    Write-Host "Building DMAP Guide $version as one file. This takes a minute or two..."
    & dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $out -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'The build failed. Read the message above.' }

    # Only the app and the guide belong in the shared folder.
    Get-ChildItem $out -File | Where-Object { $_.Name -notin @('DMAP Guide.exe', 'READ ME.txt') } | Remove-Item -Force
    Copy-Item (Join-Path $PSScriptRoot 'READ ME.txt') $out -Force
    New-Item -ItemType Directory -Force $guide | Out-Null

    # First time: offer to start the shared guide from the one on this PC.
    $settingsPath = Join-Path $env:APPDATA 'DMAP Guide\settings.json'
    if (-not (Test-Path (Join-Path $guide 'guide.json')) -and (Test-Path $settingsPath)) {
        $source = (Get-Content $settingsPath -Raw | ConvertFrom-Json).DataFolder
        if ($source -and (Test-Path (Join-Path $source 'guide.json'))) {
            $answer = Read-Host "Copy the guide on this PC ($source) into the shared folder? Type Y to copy"
            if ($answer -match '^[Yy]') {
                Copy-Item (Join-Path $source '*') $guide -Recurse -Force
                Remove-Item (Join-Path $guide 'locks') -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host 'Copied.'
            }
        }
    }

    $size = [math]::Round((Get-Item (Join-Path $out 'DMAP Guide.exe')).Length / 1MB)
    Write-Host ''
    Write-Host "Done. The shared folder is ready at:" -ForegroundColor Green
    Write-Host "  $out   (DMAP Guide.exe is version $version, $size MB)"
    Write-Host ''
    Write-Host 'Next:'
    Write-Host '  1. Copy the whole "DMAP Guide" folder into your work OneDrive.'
    Write-Host '  2. Share that folder: "Can view" for readers, "Can edit" for the people who maintain the guide.'
    Write-Host '  3. Colleagues open the shared folder in OneDrive, choose "Add shortcut to My files" so it syncs'
    Write-Host '     to their PC, then double-click "DMAP Guide.exe". READ ME.txt in the folder says the same.'
    Write-Host '  4. To ship an update, run this again and copy the new "DMAP Guide.exe" over the old one.'
    Write-Host ''
    Write-Host 'Try it on one work PC first: an unsigned .exe may get a "Windows protected your PC" prompt'
    Write-Host '(More info > Run anyway), and some workplaces block unsigned programs altogether.'
}
catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
