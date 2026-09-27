#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
output=${1:?Usage: prepare-release.sh output-directory [osx-arm64-or-osx-x64]}
: "${MACOS_SIGNING_IDENTITY:?Set MACOS_SIGNING_IDENTITY to your Developer ID Application identity}"
[[ $(uname -s) == Darwin ]] || { echo 'Signing requires macOS.' >&2; exit 1; }
mkdir -p "$output"
output=$(cd "$output" && pwd)
cd "$root"
# Shared libraries build without a RID; restore that graph before architecture-specific publishing.
dotnet restore src/OmniLyrics.Gui -p:RuntimeIdentifier=
version=$(dotnet msbuild src/OmniLyrics.Gui/OmniLyrics.Gui.csproj -getProperty:Version)
rids=(osx-arm64 osx-x64)
if [[ $# -gt 1 ]]; then rids=("$2"); fi
for rid in "${rids[@]}"; do
    [[ "$rid" == osx-arm64 || "$rid" == osx-x64 ]] || { echo 'Unsupported architecture.' >&2; exit 1; }
    [[ ! -e "$output/$rid" ]] || { echo "Output already exists: $output/$rid" >&2; exit 1; }
    dotnet publish src/OmniLyrics.Gui -c Release -r "$rid" --self-contained true \
        -p:IncludeNativeLibrariesForSelfExtract=false -o "$output/$rid/publish"
    bash build/macos/package.sh "$output/$rid/publish" "$output/$rid/package" "$version"
done
