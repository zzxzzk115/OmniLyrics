#!/usr/bin/env bash
# Install a verified packaging tool into a caller-selected directory, never system paths.
set -euo pipefail
out=${1:?Usage: install-nfpm.sh OUTPUT_DIRECTORY}
case "$(uname -m)" in
  x86_64) arch=x86_64; sha=0660ca602b2d2d2ae4781a06c692b3eeb9d437ffea05b831d76e41f4a3188783 ;;
  aarch64|arm64) arch=arm64; sha=1c0f5f2999b9a974bfb04fdb0cc3306096de530ac5dbb25d739cc5f5219c919c ;;
  *) echo 'nFPM bootstrap supports Linux x64 and ARM64.' >&2; exit 1 ;;
esac
[[ $(uname -s) == Linux ]] || { echo 'Linux is required.' >&2; exit 1; }
mkdir -p "$out"
archive=$(mktemp)
trap 'rm -f "$archive"' EXIT
curl -fsSL --retry 3 "https://github.com/goreleaser/nfpm/releases/download/v2.47.0/nfpm_2.47.0_Linux_${arch}.tar.gz" -o "$archive"
printf '%s  %s\n' "$sha" "$archive" | sha256sum --check --status
tar -xzf "$archive" --no-same-owner -C "$out" nfpm
"$out/nfpm" --version
