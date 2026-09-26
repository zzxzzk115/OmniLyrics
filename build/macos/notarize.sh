#!/usr/bin/env bash
set -euo pipefail
mode=${1:?Usage: notarize.sh submit-or-finish app-bundle keychain-profile}
app=${2:?Missing app bundle}
profile=${3:?Missing notarytool keychain profile}
credentials=(--keychain-profile "$profile")
if [[ -n "${MACOS_NOTARY_KEYCHAIN:-}" ]]; then
    credentials+=(--keychain "$MACOS_NOTARY_KEYCHAIN")
fi
app=$(cd "$(dirname "$app")" && pwd)/$(basename "$app")
output=$(dirname "$app")
submission="$output/notarization-submission.json"
case "$mode" in
submit)
    [[ ! -e "$submission" ]] || { echo "Submission already exists: $submission; use finish." >&2; exit 1; }
    codesign --verify --deep --strict "$app"
    metadata=$(codesign --display --verbose=4 "$app" 2>&1)
    [[ "$metadata" == *'Authority=Developer ID Application:'* && "$metadata" == *'Timestamp='* ]] || {
        echo 'Sign with Developer ID and a secure timestamp before notarization.' >&2; exit 1;
    }
    ditto -c -k --sequesterRsrc --keepParent "$app" "$output/notarization-upload.zip"
    xcrun notarytool submit "$output/notarization-upload.zip" "${credentials[@]}" --output-format json > "$submission.tmp"
    mv "$submission.tmp" "$submission"
    python3 -c 'import json,sys; print("Submitted:",json.load(open(sys.argv[1]))["id"])' "$submission"
    ;;
finish)
    id=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$submission")
    xcrun notarytool info "$id" "${credentials[@]}" --output-format json > "$output/notarization-result.json"
    status=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["status"])' "$output/notarization-result.json")
    if [[ "$status" == 'In Progress' ]]; then echo 'Notarization is still in progress. Run finish again later.'; exit 2; fi
    if [[ "$status" != 'Accepted' ]]; then
        xcrun notarytool log "$id" "${credentials[@]}" "$output/notarization-log.json"
        echo "Notarization $status. See $output/notarization-log.json" >&2; exit 1
    fi
    # A bundle that was launched for testing may be protected against changes
    # by macOS. Staple and archive a fresh copy, leaving that bundle untouched.
    staging=$(mktemp -d "${TMPDIR:-/tmp}/omnilyrics-staple.XXXXXX")
    trap 'rm -rf "$staging"' EXIT
    ditto "$app" "$staging/OmniLyrics.app"
    xcrun stapler staple "$staging/OmniLyrics.app"
    xcrun stapler validate "$staging/OmniLyrics.app"
    codesign --verify --deep --strict "$staging/OmniLyrics.app"
    spctl --assess --type execute --verbose=2 "$staging/OmniLyrics.app"
    # Recreate the distribution archive AFTER stapling, so offline installs receive the ticket.
    ditto -c -k --sequesterRsrc --keepParent "$staging/OmniLyrics.app" "$output/OmniLyrics.app.zip"
    shasum -a 256 "$output/OmniLyrics.app.zip"
    ;;
*) echo 'Mode must be submit or finish.' >&2; exit 1 ;;
esac
