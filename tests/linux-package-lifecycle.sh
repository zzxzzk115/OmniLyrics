#!/usr/bin/env bash
# Run only inside a disposable container; /packages is mounted read-only.
set -euo pipefail
[[ -f /.dockerenv ]] || { echo 'Use a disposable Docker container.' >&2; exit 1; }
format=${1:?Usage: linux-package-lifecycle.sh deb|rpm|archlinux VERSION}
version=${2:?Missing version}
case "$(uname -m)" in
  x86_64) debarch=amd64; nativearch=x86_64 ;;
  aarch64) debarch=arm64; nativearch=aarch64 ;;
  *) exit 1 ;;
esac
export OMNILYRICS_CONFIG_DIR=/tmp/omnilyrics-test-config
export DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/omnilyrics-test-bundle
mkdir -p "$OMNILYRICS_CONFIG_DIR"
printf 'preserve user configuration\n' > "$OMNILYRICS_CONFIG_DIR/keep.txt"
package_root() { if [[ $1 == 2 ]]; then printf /upgrades; else printf /packages; fi; }
case "$format" in
  deb)
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq python3 xvfb xauth desktop-file-utils fonts-dejavu-core
    package_file() { printf '%s/%s_%s-%s_%s.deb' "$(package_root "$2")" "$1" "$version" "$2" "$debarch"; }
    install_package() { apt-get install -y -qq "$(package_file "$1" "$2")"; }
    remove_package() { apt-get purge -y -qq "$1"; }
    installed_version() { dpkg-query -W -f='${Version}' "$1"; }
    ;;
  rpm)
    dnf install -y -q python3 xorg-x11-server-Xvfb xorg-x11-xauth desktop-file-utils dejavu-sans-fonts
    package_file() { printf '%s/%s-%s-%s.%s.rpm' "$(package_root "$2")" "$1" "$version" "$2" "$nativearch"; }
    install_package() { dnf install -y -q "$(package_file "$1" "$2")"; }
    remove_package() { dnf remove -y -q "$1"; }
    installed_version() { rpm -q --qf '%{VERSION}-%{RELEASE}' "$1"; }
    ;;
  archlinux)
    pacman -Syu --noconfirm --needed python xorg-server-xvfb xorg-xauth desktop-file-utils ttf-dejavu
    package_file() { printf '%s/%s-%s-%s-%s.pkg.tar.zst' "$(package_root "$2")" "$1" "$version" "$2" "$nativearch"; }
    install_package() { pacman -U --noconfirm "$(package_file "$1" "$2")"; }
    remove_package() { pacman -R --noconfirm "$1"; }
    installed_version() { pacman -Q "$1" | cut -d' ' -f2; }
    ;;
  *) exit 1 ;;
esac
# The CLI must work without the GUI or a separately installed .NET runtime.
if command -v dotnet; then echo "Unexpected external .NET runtime" >&2; exit 1; fi
install_package omnilyrics-cli 1
[[ $(installed_version omnilyrics-cli) == "$version-1" ]]
omnilyrics-cli config show > /tmp/cli-output
[[ ! -e /usr/bin/omnilyrics ]]
[[ ! -e /usr/share/applications/io.github.zzxzzk115.OmniLyrics.desktop ]]
install_package omnilyrics 1
[[ $(installed_version omnilyrics) == "$version-1" ]]
[[ $(readlink /usr/bin/omnilyrics) == /usr/lib/omnilyrics/OmniLyrics.Gui ]]
[[ $(readlink /usr/bin/omnilyrics-cli) == /usr/lib/omnilyrics-cli/OmniLyrics.Cli ]]
desktop-file-validate /usr/share/applications/io.github.zzxzzk115.OmniLyrics.desktop
for size in 16 32 128 256 512; do
  test -f "/usr/share/icons/hicolor/${size}x${size}/apps/io.github.zzxzzk115.OmniLyrics.png"
done
# Verify package upgrades, independent removal, and preservation of per-user settings.
install_package omnilyrics-cli 2
install_package omnilyrics 2
[[ $(installed_version omnilyrics-cli) == "$version-2" ]]
[[ $(installed_version omnilyrics) == "$version-2" ]]
omnilyrics-cli config show > /tmp/cli-output
remove_package omnilyrics-cli
[[ ! -e /usr/bin/omnilyrics-cli && ! -e /usr/lib/omnilyrics-cli/OmniLyrics.Cli ]]
[[ -x /usr/bin/omnilyrics ]]
# Native GUI smoke on a private display: no access to the host compositor or D-Bus.
Xvfb :98 -screen 0 1280x800x24 > /tmp/xvfb.log 2>&1 &
xvfb_pid=$!
trap 'kill "$xvfb_pid" 2>/dev/null || true' EXIT
export DISPLAY=:98
python3 - <<'PY'
import os, pathlib, subprocess, time
for _ in range(50):
    if pathlib.Path('/tmp/.X11-unix/X98').exists():
        break
    time.sleep(0.1)
else:
    raise SystemExit('Xvfb did not start')
with open('/tmp/gui.log', 'w+') as log:
    process = subprocess.Popen(['/usr/bin/omnilyrics', '--settings'], stdout=log, stderr=log)
    try:
        try:
            status = process.wait(timeout=8)
        except subprocess.TimeoutExpired:
            print('Installed GUI started without CLI or external .NET runtime')
        else:
            log.seek(0)
            raise SystemExit(f'Installed GUI exited early ({status}):\n' + log.read())
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
PY
install_package omnilyrics-cli 2
remove_package omnilyrics
[[ ! -e /usr/bin/omnilyrics && ! -e /usr/lib/omnilyrics/OmniLyrics.Gui ]]
[[ ! -e /usr/share/applications/io.github.zzxzzk115.OmniLyrics.desktop ]]
[[ ! -e /usr/share/icons/hicolor/128x128/apps/io.github.zzxzzk115.OmniLyrics.png ]]
omnilyrics-cli config show > /tmp/cli-output
remove_package omnilyrics-cli
[[ $(cat "$OMNILYRICS_CONFIG_DIR/keep.txt") == 'preserve user configuration' ]]
echo "PASS $format $nativearch: independent install, upgrade, GUI startup and removal; settings preserved"
