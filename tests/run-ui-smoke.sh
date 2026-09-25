#!/usr/bin/env bash
set -euo pipefail

# Map/resize tests belong on a private X server, never on the user's desktop.
for tool in Xvfb openbox dbus-run-session; do
    command -v "$tool" >/dev/null || { echo "Missing UI test dependency: $tool" >&2; exit 2; }
done
if [[ ${OMNILYRICS_UI_TEST_BUS:-} != 1 ]]; then
    exec env OMNILYRICS_UI_TEST_BUS=1 dbus-run-session -- bash "$0" "$@"
fi
cd "$(dirname "$0")/.."
dotnet build tests/UiSmoke/UiSmoke.csproj -c Release -m:1 -nr:false
runtime=$(mktemp -d)
wm_pid=
x_pid=
cleanup() {
    [[ -z $wm_pid ]] || kill "$wm_pid" 2>/dev/null || true
    [[ -z $x_pid ]] || kill "$x_pid" 2>/dev/null || true
    rm -rf "$runtime"
}
trap cleanup EXIT
# Stay clear of Xwayland's usual :0/:1 addresses, even if it starts lazily.
test_display=":$((100 + RANDOM % 1000))"
Xvfb "$test_display" -displayfd 3 -screen 0 2560x1600x24 -nolisten tcp 3>"$runtime/display" >"$runtime/x.log" 2>&1 &
x_pid=$!
for ((i=0; i<100; i++)); do
    [[ ! -s $runtime/display ]] || break
    kill -0 "$x_pid" 2>/dev/null || { cat "$runtime/x.log" >&2; exit 1; }
    sleep .05
done
[[ -s $runtime/display ]] || { echo 'Virtual X server did not start.' >&2; exit 1; }
export OMNILYRICS_UI_TEST_DISPLAY=":$(cat "$runtime/display")"
printf '%s\n' '<openbox_config xmlns="http://openbox.org/3.4/rc"><focus><followMouse>no</followMouse></focus></openbox_config>' >"$runtime/openbox.xml"
env -u WAYLAND_DISPLAY -u HYPRLAND_INSTANCE_SIGNATURE DISPLAY="$OMNILYRICS_UI_TEST_DISPLAY" \
    openbox --sm-disable --config-file "$runtime/openbox.xml" >"$runtime/wm.log" 2>&1 &
wm_pid=$!
sleep .5
kill -0 "$wm_pid" 2>/dev/null || { cat "$runtime/wm.log" >&2; exit 1; }
env -u WAYLAND_DISPLAY -u HYPRLAND_INSTANCE_SIGNATURE DISPLAY="$OMNILYRICS_UI_TEST_DISPLAY" \
    OMNILYRICS_UI_TEST_XSERVER_PID="$x_pid" tests/UiSmoke/bin/Release/net10.0/linux-x64/UiSmoke "$@"
