#!/usr/bin/env bash
set -euo pipefail
app=${1:?Usage: sign-app.sh app-bundle signing-identity}
identity=${2:?Missing Developer ID Application identity}
root=$(cd "$(dirname "$0")/../.." && pwd)
[[ -d "$app/Contents/MacOS" ]] || { echo 'Missing app bundle' >&2; exit 1; }
main="$app/Contents/MacOS/OmniLyrics.Gui"
# .NET statically links its runtime into the single-file host on macOS. Avalonia's
# native dependencies must remain external so they can be signed individually.
for library in libAvaloniaNative.dylib libSkiaSharp.dylib libHarfBuzzSharp.dylib; do
    [[ -f "$app/Contents/MacOS/$library" ]] || {
        echo 'Publish with IncludeNativeLibrariesForSelfExtract=false before signing.' >&2; exit 1;
    }
done
while IFS= read -r -d '' binary; do
    [[ "$binary" != "$main" ]] || continue
    if [[ $(file -b "$binary") == Mach-O* ]]; then
        codesign --force --timestamp --options runtime --sign "$identity" "$binary"
    fi
done < <(find "$app/Contents" -type f -print0)
codesign --force --timestamp --options runtime --entitlements "$root/build/macos/entitlements.plist" --sign "$identity" "$main"
codesign --force --timestamp --options runtime --entitlements "$root/build/macos/entitlements.plist" --sign "$identity" "$app"
# --deep is used for verification only; each nested Mach-O was signed explicitly.
codesign --verify --deep --strict --verbose=2 "$app"
metadata=$(codesign --display --verbose=4 "$app" 2>&1)
[[ "$metadata" == *'Authority=Developer ID Application:'* && "$metadata" == *'runtime'* && "$metadata" == *'Timestamp='* ]] || {
    echo 'A timestamped Developer ID Application signature with Hardened Runtime is required.' >&2; exit 1;
}
