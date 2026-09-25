#!/usr/bin/env bash
set -euo pipefail
# Keep the portable single executable, and offer a Finder-ready app alongside it.
root=$(cd "$(dirname "$0")/../.." && pwd)
binary=${1:?Usage: package.sh published-executable output-directory version}
output=${2:?Missing output directory}
version=${3:?Missing version}
app="$output/OmniLyrics.app"
if [[ -e "$app" ]]; then
    echo "Refusing to overwrite an existing app: $app" >&2
    exit 1
fi
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp "$binary" "$app/Contents/MacOS/OmniLyrics.Gui"
chmod +x "$app/Contents/MacOS/OmniLyrics.Gui"
cp "$root/src/OmniLyrics.Gui/Assets/app.icns" "$app/Contents/Resources/app.icns"
cp "$root/build/macos/Info.plist" "$app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $version" "$app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"
# Ad-hoc signing preserves local app integrity; this is not Developer ID notarization.
codesign --force --sign - "$app"
codesign --verify --strict "$app"
ditto -c -k --sequesterRsrc --keepParent "$app" "$output/OmniLyrics.app.zip"
