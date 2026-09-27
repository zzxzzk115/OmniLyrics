#!/usr/bin/env bash
set -euo pipefail
# Keep the portable single executable, and offer a Finder-ready app alongside it.
root=$(cd "$(dirname "$0")/../.." && pwd)
source=${1:?Usage: package.sh published-executable-or-directory output-directory version}
output=${2:?Missing output directory}
version=${3:?Missing version}
app="$output/OmniLyrics.app"
if [[ -e "$app" ]]; then
    echo "Refusing to overwrite an existing app: $app" >&2
    exit 1
fi
if [[ -n "${MACOS_SIGNING_IDENTITY:-}" && ! -d "$source" ]]; then
    echo 'Formal signing requires a publish directory with separate native libraries.' >&2
    exit 1
fi
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
if [[ -d "$source" ]]; then
    ditto "$source" "$app/Contents/MacOS"
else
    cp "$source" "$app/Contents/MacOS/OmniLyrics.Gui"
fi
chmod +x "$app/Contents/MacOS/OmniLyrics.Gui"
cp "$root/src/OmniLyrics.Gui/Assets/app.icns" "$app/Contents/Resources/app.icns"
cp "$root/build/macos/Info.plist" "$app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $version" "$app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"
if [[ -n "${MACOS_SIGNING_IDENTITY:-}" ]]; then
    bash "$root/build/macos/sign-app.sh" "$app" "$MACOS_SIGNING_IDENTITY"
else
    # CI/preview packages have no private signing identity and are explicitly ad-hoc.
    codesign --force --sign - "$app"
    codesign --verify --strict "$app"
fi
ditto -c -k --sequesterRsrc --keepParent "$app" "$output/OmniLyrics.app.zip"
