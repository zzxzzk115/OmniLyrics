#!/usr/bin/env bash
# /recipes contains generated PKGBUILD directories for one real upstream release.
set -euo pipefail
[[ -f /.dockerenv ]] || { echo 'Use a disposable Docker container.' >&2; exit 1; }
pacman -Syu --noconfirm --needed python
useradd --create-home builder
cp -r /recipes /tmp/aur
chown -R builder:builder /tmp/aur
export OMNILYRICS_CONFIG_DIR=/tmp/omnilyrics-aur-config
export DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/omnilyrics-aur-bundle
for name in omnilyrics omnilyrics-cli; do
  folder="/tmp/aur/$name-bin"
  # shellcheck disable=SC2016 # $1 is intentionally expanded by the child shell.
  runuser -u builder -- bash -c 'cd "$1" && makepkg --verifysource --noconfirm && makepkg --nodeps --noconfirm' bash "$folder"
  # shellcheck disable=SC2016 # $1 is intentionally expanded by the child shell.
  runuser -u builder -- bash -c 'cd "$1" && makepkg --printsrcinfo' bash "$folder" > /tmp/srcinfo
  python - "$folder/.SRCINFO" <<'PY'
import pathlib, sys
normalize = lambda s: sorted(line.strip() for line in s.splitlines() if line.strip())
assert normalize(pathlib.Path(sys.argv[1]).read_text()) == normalize(pathlib.Path('/tmp/srcinfo').read_text())
PY
  files=("$folder"/*.pkg.tar.zst)
  [[ ${#files[@]} == 1 ]]
  pacman -U --noconfirm "${files[0]}"
  binary=OmniLyrics.Gui
  [[ $name == omnilyrics-cli ]] && binary=OmniLyrics.Cli
  cmp "$folder/src/$binary" "/usr/lib/$name/$binary"
done
omnilyrics-cli config show
pacman -R --noconfirm omnilyrics-bin
[[ ! -e /usr/bin/omnilyrics && -x /usr/bin/omnilyrics-cli ]]
omnilyrics-cli config show
pacman -R --noconfirm omnilyrics-cli-bin
[[ ! -e /usr/bin/omnilyrics-cli ]]
echo 'PASS AUR: release checksums, makepkg metadata, unmodified single-file payloads, separate installs/removal'
