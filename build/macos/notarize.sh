#!/usr/bin/env bash
set -euo pipefail
mode=${1:?Usage: notarize.sh submit-or-finish app-bundle keychain-profile}
app=${2:?Missing app bundle}
profile=${3:?Missing notarytool keychain profile}
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
    xcrun notarytool submit "$output/notarization-upload.zip" --keychain-profile "$profile" --output-format json > "$submission.tmp"
    mv "$submission.tmp" "$submission"
    python3 -c 'import json,sys; print("Submitted:",json.load(open(sys.argv[1]))["id"])' "$submission"
    ;;
finish)
    id=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$submission")
    xcrun notarytool info "$id" --keychain-profile "$profile" --output-format json > "$output/notarization-result.json"
    status=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["status"])' "$output/notarization-result.json")
    if [[ "$status" == 'In Progress' ]]; then echo 'Notarization is still in progress. Run finish again later.'; exit 2; fi
    if [[ "$status" != 'Accepted' ]]; then
        xcrun notarytool log "$id" --keychain-profile "$profile" "$output/notarization-log.json"
        echo "Notarization $status. See $output/notarization-log.json" >&2; exit 1
    fi
    xcrun stapler staple "$app"
    xcrun stapler validate "$app"
    codesign --verify --deep --strict "$app"
    spctl --assess --type execute --verbose=2 "$app"
    # Recreate the distribution archive AFTER stapling, so offline installs receive the ticket.
    ditto -c -k --sequesterRsrc --keepParent "$app" "$output/OmniLyrics.app.zip"
    shasum -a 256 "$output/OmniLyrics.app.zip"
    ;;
*) echo 'Mode must be submit or finish.' >&2; exit 1 ;;
esac
