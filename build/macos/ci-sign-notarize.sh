#!/usr/bin/env bash
set -euo pipefail
# This script runs only on the disposable signing runner, never on a personal Mac.
[[ "${GITHUB_ACTIONS:-}" == true && $(uname -s) == Darwin ]] || {
    echo 'Use notarize.sh for local signing; this script requires a GitHub macOS runner.' >&2
    exit 1
}
: "${RUNNER_TEMP:?}"
: "${MACOS_CERTIFICATE_P12_BASE64:?Missing certificate Secret}"
: "${MACOS_CERTIFICATE_PASSWORD:?Missing certificate password Secret}"
: "${APPLE_ID:?Missing Apple account Secret}"
: "${APPLE_APP_SPECIFIC_PASSWORD:?Missing notarization password Secret}"
: "${APPLE_TEAM_ID:?Missing team variable}"
: "${MACOS_SIGNING_IDENTITY:?Missing signing identity variable}"
: "${RELEASE_TAG:?}"
[[ "$RELEASE_TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
umask 077
keychain="$RUNNER_TEMP/omnilyrics-signing.keychain-db"
certificate="$RUNNER_TEMP/omnilyrics-signing.p12"
[[ ! -e "$keychain" && ! -e "$certificate" ]]
cleanup() {
    if [[ -f "$keychain" ]]; then security delete-keychain "$keychain" || true; fi
    rm -f "$certificate"
}
trap cleanup EXIT
keychain_password=$(openssl rand -hex 32)
echo "::add-mask::$keychain_password"
printf '%s' "$MACOS_CERTIFICATE_P12_BASE64" | base64 --decode > "$certificate"
security create-keychain -p "$keychain_password" "$keychain"
security set-keychain-settings -lut 3600 "$keychain"
security unlock-keychain -p "$keychain_password" "$keychain"
security import "$certificate" -P "$MACOS_CERTIFICATE_PASSWORD" -k "$keychain" -T /usr/bin/codesign > /dev/null
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$keychain_password" "$keychain" > /dev/null
security list-keychains -d user -s "$keychain"
rm -f "$certificate"
unset MACOS_CERTIFICATE_P12_BASE64 MACOS_CERTIFICATE_PASSWORD keychain_password
export MACOS_NOTARY_KEYCHAIN="$keychain"
profile=omnilyrics-ci
xcrun notarytool store-credentials "$profile" --keychain "$keychain" \
    --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_SPECIFIC_PASSWORD" > /dev/null
unset APPLE_ID APPLE_APP_SPECIFIC_PASSWORD
for rid in osx-arm64 osx-x64; do
    package="$root/signed-work/$rid/package"
    ditto -x -k "unsigned/$rid.zip" "$package"
    app="$package/OmniLyrics.app"
    version=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$app/Contents/Info.plist")
    [[ "v$version" == "$RELEASE_TAG" ]] || { echo 'Unexpected artifact version'; exit 1; }
    bash build/macos/sign-app.sh "$app" "$MACOS_SIGNING_IDENTITY"
    metadata=$(codesign --display --verbose=4 "$app" 2>&1)
    [[ "$metadata" == *"TeamIdentifier=$APPLE_TEAM_ID"* ]] || { echo 'Signing team mismatch'; exit 1; }
    bash build/macos/notarize.sh submit "$app" "$profile"
done
for rid in osx-arm64 osx-x64; do
    package="$root/signed-work/$rid/package"
    id=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$package/notarization-submission.json")
    # finish checks acceptance and downloads the diagnostic log on rejection.
    xcrun notarytool wait "$id" --keychain-profile "$profile" --keychain "$keychain" --timeout 25m || true
    bash build/macos/notarize.sh finish "$package/OmniLyrics.app" "$profile"
done
python3 build/homebrew/make-cask.py "$root/signed-work" "$root/release-assets"
