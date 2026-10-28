$ErrorActionPreference = 'Stop'

# Packs what a colleague needs to build DMAP Guide for the Mac, on their Mac, into one zip:
#
#   publish\DMAP Guide - Mac build kit <version>.zip
#       MAC BUILD - START HERE.txt   the steps
#       Build Mac App.command        builds the app and packs "DMAP Guide for Mac.zip"
#       DmapShared, DmapAndroid      the code (the Mac app is DmapAndroid built for Mac Catalyst)
#       DmapGuide\...                only the fonts, pictures and version number the Mac app uses
#
# The zip is written entry by entry so it unzips correctly on a Mac (forward slashes, and the
# build script marked as runnable).

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Get-Content (Join-Path $repoRoot 'DmapGuide\DmapGuide.csproj') -Raw
if ($csproj -notmatch '<Version>([0-9.]+)</Version>') { throw 'DmapGuide.csproj has no <Version>.' }
$version = $Matches[1]
$outFolder = Join-Path $repoRoot 'publish'
$zipPath = Join-Path $outFolder "DMAP Guide - Mac build kit $version.zip"

# Folders and files to include, relative to the repo; build output and stray temp files are left out.
$folders = @('DmapShared', 'DmapAndroid', 'DmapGuide\Assets', 'DmapGuide\Fonts')
$files = @('MAC BUILD - START HERE.txt', 'Build Mac App.command', 'DmapGuide\DmapGuide.csproj')
$skip = '\\(bin|obj)\\|\.Backup\.tmp$|\.user$'

try {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Force $outFolder | Out-Null
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    $entries = New-Object System.Collections.Generic.List[string]
    foreach ($f in $folders) {
        Get-ChildItem (Join-Path $repoRoot $f) -Recurse -File |
            ForEach-Object { $_.FullName.Substring($repoRoot.Length + 1) } |
            Where-Object { $_ -notmatch $skip } |
            ForEach-Object { $entries.Add($_) }
    }
    $files | ForEach-Object { $entries.Add($_) }

    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        foreach ($rel in $entries) {
            $name = 'DMAP Guide Mac build kit/' + ($rel -replace '\\', '/')
            $entry = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $repoRoot $rel), $name)
            # Unix permissions in the top 16 bits: the build script runnable, everything else plain.
            $mode = if ($rel -like '*.command') { 0x81ED } else { 0x81A4 }   # rwxr-xr-x / rw-r--r--
            $entry.ExternalAttributes = $mode -shl 16
        }
    }
    finally {
        $zip.Dispose()
    }

    $size = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host ''
    Write-Host "Done. The Mac build kit is ready:" -ForegroundColor Green
    Write-Host "  $zipPath   (version $version, $size MB, $($entries.Count) files)"
    Write-Host ''
    Write-Host 'Send it to the colleague with the Mac. "MAC BUILD - START HERE.txt" inside tells them what to do;'
    Write-Host 'they put "DMAP Guide for Mac.zip" in the shared OneDrive folder when they are done.'
}
catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
