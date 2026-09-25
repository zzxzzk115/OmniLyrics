# Quickshell lyric service

`end4-ii.patch` contains the widget changes for the local end4 `ii` configuration:
wider bar/popup, lyric labels, favorite button and popup dismissal fix. Apply it
to a compatible end4 checkout after reviewing the diff; other revisions may need
the same changes adapted by hand. Copy the service/helper files described below
alongside the patched widgets. The patch contains no credentials.

`OmniLyrics.qml` is a singleton adapter for the existing end4 `ii` shell. It owns one
`OmniLyrics.Cli --mode json` subprocess, consumes its current/next lyric snapshots,
and restarts the process if it exits. The child exits with the shell. JSON mode reuses an existing service. If none is
available, it participates as the lowest-priority CLI candidate in the shared
GUI → TUI → CLI election and may own the HTTP/UDP listeners.

Install the QML file as `services/OmniLyrics.qml` in the shell configuration and
import `qs.services` in a music widget. Bind to `OmniLyrics.currentFor(player)` and
`OmniLyrics.nextFor(player)`. These functions verify the source player and song
metadata before displaying lyrics. Use `textFormat: Text.PlainText` on lyric labels.

The default executable path is
`~/GitHub/OmniLyrics/src/OmniLyrics.Cli/bin/Release/net10.0/linux-x64/OmniLyrics.Cli`.
Set `OMNILYRICS_EXECUTABLE` in the shell's environment to override it. Build the CLI
before starting the shell. `qs ipc -c ii call lyrics status` reports the connection
and latest snapshot.

The local end4 integration displays the current line beneath the song title in the
bar, and the current/next lines below the expanded music controls. Missing lyrics
fall back to song information and a placeholder. On Linux, Cider V4 works through
MPRIS even when its dedicated API requires authentication. Select
`config cider integration mpris` for V4 on Linux, or `webapi` for V3's incomplete
MPRIS implementation. The default `auto` prefers a working Cider Web API.

## Cider V4 favorites

Install `CiderFavorites.qml` as `services/CiderFavorites.qml` and
`cider_favorites.py` as `scripts/cider_favorites.py`. Python 3 is required; there
are no additional Python packages. Import `qs.services`, show the button for
`CiderFavorites.isCider(player)`, bind its filled star to
`CiderFavorites.isFavorite(player)`, and call `CiderFavorites.toggle(player)`.
Use `CiderFavorites.matches(player) && !CiderFavorites.busy` for the enabled
state and `CiderFavorites.label(player)` for its tooltip/accessibility label.

Use OmniLyrics' **Settings** page or `OmniLyrics.Cli config` to select the shared
authentication mode. With Cider's **Require API Tokens** enabled, save a token
with `playback` and `library` scopes. When Cider authentication is disabled,
choose **No token** / `config cider none`; favorites work without credentials.
None mode ignores any old token file, so it does not have to be deleted.

On Linux the helper reads `~/.config/omnilyrics/config.json` and `cider-token` on
each request. `OMNILYRICS_CONFIG_DIR` overrides the directory. `CIDER_AUTH_MODE`
overrides the saved mode; in token mode, `CIDER_API_TOKEN` takes precedence over
`CIDER_TOKEN_FILE` and then the default token file. Credentials are never passed
in process arguments, emitted in status output, or stored in QML.

For V4 on Linux, the lyric service can use MPRIS without a Cider token.
See the [shared configuration guide](../../docs/user-guide.md#cider-v3-support-and-configuration)
for CLI/terminal/GUI setup and environment precedence. Playback targets V3+;
the favorite adapter specifically targets V4's API.

Favorite status refreshes every four seconds while Cider is present. Only user
changes show a busy state; background reads do not animate
or disable the button. A click during a read is queued and checked again before
writing. Writes
recheck the current track ID and wait for the rating to be read back before
showing success. Authentication failures, unavailable Cider and unconfirmed
updates are shown in the button tooltip. `qs ipc -c ii call ciderFavorites status`
reports the latest state without exposing credentials.

Run the local mock tests with:

```bash
python3 -m unittest discover -s tests -p 'test_*.py' -v
# Optional: exercise the actual QML service with a fake player and API.
python3 tests/quickshell_favorites_smoke.py --qs /path/to/qs
```

When wiring the end4 popup's IPC `open`, `close` and `toggle` actions, update
`GlobalStates.mediaControlsOpen`. Assigning `Loader.active` directly removes its
binding, preventing outside-click dismissal from closing the panel.
