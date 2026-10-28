#!/bin/bash
# Builds DMAP Guide for the Mac and packs it as "DMAP Guide for Mac.zip", ready to put in the
# shared OneDrive folder. Run it on a Mac from Terminal:   bash "Build Mac App.command"
# (double-clicking also works once the file is allowed to run: chmod +x "Build Mac App.command").
#
# Optional signing (needs a paid Apple Developer account, see MAC BUILD - START HERE.txt):
#   DMAP_SIGN_IDENTITY="Developer ID Application: Name (TEAMID)" bash "Build Mac App.command"
set -euo pipefail

pause() { read -r -p "Press Return to close." _ || true; }
fail() { echo; echo "STOPPED: $1"; echo; pause; exit 1; }

[[ "$(uname -s)" == "Darwin" ]] || fail "this must be run on a Mac."
xcode-select -p >/dev/null 2>&1 || fail "Xcode isn't set up. Install Xcode from the App Store, open it once, then run this again."
command -v dotnet >/dev/null 2>&1 || fail "the .NET 10 SDK isn't installed. Get the macOS installer from https://dotnet.microsoft.com/download/dotnet/10.0 and run this again."
[[ "$(dotnet --version)" == 10.* ]] || fail "this needs the .NET 10 SDK (found $(dotnet --version)). Install it from https://dotnet.microsoft.com/download/dotnet/10.0"

here="$(cd "$(dirname "$0")" && pwd)"
project="$here/DmapAndroid/DmapAndroid.csproj"
out="$here/Mac app"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$here/DmapGuide/DmapGuide.csproj" | head -1)"

if ! dotnet workload list | grep -q 'maui-maccatalyst'; then
    echo "Installing the Mac app build support (once; your Mac password may be asked for)..."
    sudo dotnet workload install maui-maccatalyst || fail "the Mac app build support couldn't be installed (see the message above)."
fi

signing=()
if [[ -n "${DMAP_SIGN_IDENTITY:-}" ]]; then
    [[ -n "${DMAP_SIGN_PROFILE:-}" ]] || fail "signing needs DMAP_SIGN_PROFILE too (the name of the Developer ID provisioning profile)."
    echo "Signing with: $DMAP_SIGN_IDENTITY (profile: $DMAP_SIGN_PROFILE)"
    # Microsoft's documented way to sign a Mac Catalyst app for sharing outside the App Store.
    signing=(-p:EnableCodeSigning=true "-p:CodesignKey=$DMAP_SIGN_IDENTITY" "-p:CodesignProvision=$DMAP_SIGN_PROFILE"
             -p:UseHardenedRuntime=true -p:DmapSkipCodesignVerify=true)
fi

echo "Building DMAP Guide $version for the Mac (Apple-chip and Intel Macs). This takes a few minutes..."
rm -rf "$here/DmapAndroid/bin/Release/net10.0-maccatalyst"
dotnet publish "$project" -f net10.0-maccatalyst -c Release \
    -p:DmapBuildMacCatalyst=true \
    '-p:RuntimeIdentifiers=maccatalyst-x64;maccatalyst-arm64' \
    -p:CreatePackage=false \
    ${signing[@]+"${signing[@]}"} \
    || fail "the build failed. Send the messages above to whoever looks after DMAP Guide."

app="$(find "$here/DmapAndroid/bin/Release/net10.0-maccatalyst" -maxdepth 3 -type d -name 'DMAP Guide.app' -print -quit)"
[[ -n "$app" ]] || fail "the build finished but DMAP Guide.app wasn't found."

if [[ -z "${DMAP_SIGN_IDENTITY:-}" ]] && ! codesign --verify "$app" >/dev/null 2>&1; then
    # No developer account: sign it "ad hoc", which Apple-chip Macs need before they'll run it at all.
    codesign --force --deep --sign - "$app" || fail "the app couldn't be prepared to run (see the message above)."
fi

mkdir -p "$out"
rm -rf "$out/DMAP Guide.app" "$out/DMAP Guide for Mac.zip"
cp -R "$app" "$out/"
# ditto keeps the app bundle intact (a plain zip can break it).
ditto -c -k --keepParent "$out/DMAP Guide.app" "$out/DMAP Guide for Mac.zip"

echo
echo "Done: DMAP Guide $version for the Mac."
echo "  $out/DMAP Guide.app              (open it to try it on this Mac)"
echo "  $out/DMAP Guide for Mac.zip      (put this in the shared DMAP Guide folder in OneDrive)"
if [[ -n "${DMAP_SIGN_IDENTITY:-}" ]]; then
    echo
    echo "To have Apple notarize it (so other Macs open it without a warning), see step 6 in MAC BUILD - START HERE.txt."
fi
echo
open "$out"
pause
