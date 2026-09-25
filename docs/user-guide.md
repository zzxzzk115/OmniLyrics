# User guide

English | [简体中文](./user-guide.zh-CN.md) · [README](../README.md)

For installation and build instructions, see the [README](../README.md#build-instruction).
Open the lyric window's gear button or the tray menu to access **Settings**.

## Players and platforms

| Platform | Connection | Players |
| --- | --- | --- |
| Windows | System Media Transport Controls (SMTC) | Spotify and other players exposing system media controls |
| macOS | `media-control` | Apple Music, Spotify, Cider and other players exposing Now Playing |
| Linux | MPRIS | Spotify and other MPRIS players |
| All three | Player-specific API | Cider V3+ and YesPlayMusic |

On macOS, install `media-control` with `brew install media-control`.
Playback, queue access and favorites depend on the connected player's capabilities.
The favorite button appears only when supported; the current adapter uses Cider V4's library API.

## Layouts and appearance

In **Settings → General → Interface scale**, the default follows the current monitor. Choose 100–200% or a custom 75–300% scale to override it for all windows, immediately. This does not change your saved lyric font sizes. In the configuration file, `appearance.uiScale` is `null` for system scaling or a factor such as `1.5` for 150%.

Choose a layout under **Settings → Appearance**, then click **Apply**.

| Preset | Display |
| --- | --- |
| Classic | Floating current and next lines, with an optional translation |
| Compact | Current line and optional translation in a small window |
| Focus | Album artwork and song information beside multi-line lyrics |
| Portrait | Song information above vertically arranged lyrics |
| Fullscreen | Centered lyrics and a separate song-information area; Esc returns to Focus |

Appearance settings include original/translation font sizes, background opacity,
frosted glass, and visibility of translations, the OmniLyrics logo and player information.
Frosted glass depends on the operating system; when unavailable, the selected background color and opacity are used.

**Theme & colors** provides Dark + Red, Dark + Blue, Light + Blue and Light + Red presets.
You can also customize the theme, accent, text, highlight and lyric background colors.
Name and save a custom preset to reuse it; choose **Apply** to activate it.
Color presets leave layout, font sizes, opacity and blur unchanged.

The lock button locks only the window's position. Resizing, playback, favorites and settings remain available.
Unlock with the same button, the tray menu, or **Ctrl+L** while the window has focus.
Closing the lyric window hides it to the tray; choose **Quit** in the tray menu to exit.

## Lyrics and matching

OmniLyrics prioritizes genuine word-timed lyrics across enabled sources before falling back to line-synced lyrics.
Ordinary LRC lyrics use simulated highlighting by default; disable **Simulate highlighting for line-synced lyrics**
for whole-line highlighting instead. Simulation estimates progress between line timestamps and cannot reproduce real word timing.

Enable translations in Appearance to show bilingual lyrics when a source provides them.
Missing translations remain blank; OmniLyrics does not generate translations.

Under **Settings → Lyrics & matching**, configure:

| Setting | Choices |
| --- | --- |
| Search strategy | Fallback broadens the search when initial results do not match; Basic uses fewer requests. Both prioritize word timing. |
| Matching | Standard checks title, lead artist, recording version and known durations. Strict also requires the same album and known durations within two seconds. |
| Sources | Enable player lyrics (currently YesPlayMusic), QQ Music and NetEase individually; choose QQ or NetEase as the preferred online source. |
| Prefetch | Cache upcoming songs in advance; enabled for the next 5 tracks by default, adjustable from 1 to 20. |

Changing the search or matching rules reloads the current song under the new rules.
If lyrics belong to a different recording, try Strict matching. It can reduce incorrect matches,
but may also leave more songs without lyrics. Metadata matching cannot guarantee exact audio alignment.

Prefetch requires a queue from Cider's Web API or MPRIS TrackList. Spotify Web API queues
and native Apple Music queues on macOS are not integrated. Without a supported queue,
OmniLyrics still fetches and caches the current song. Cached lyrics persist across restarts.

## CLI and shared preferences

The commands below assume `OmniLyrics.Cli` is on your path. When running from source,
replace it with `dotnet run --project src/OmniLyrics.Cli --` from the repository root.

```bash
OmniLyrics.Cli                         # Terminal lyric view (TUI)
OmniLyrics.Cli --mode line             # Single-line output for status bars
OmniLyrics.Cli --mode json             # JSON snapshots for widgets
OmniLyrics.Cli config                  # Interactive configuration
OmniLyrics.Cli config show
OmniLyrics.Cli config language auto    # auto, en or zh-CN
OmniLyrics.Cli config lyrics strategy fallback  # fallback or quick
OmniLyrics.Cli config lyrics match balanced     # balanced or strict
OmniLyrics.Cli config lyrics sources player,qq,netease
OmniLyrics.Cli config lyrics prefer qq          # qq or netease
OmniLyrics.Cli config lyrics prefetch on        # on or off
OmniLyrics.Cli config lyrics count 5
```

GUI, interactive terminal and CLI share preferences. The GUI language changes immediately
when selecting **Follow system**, **English** or **简体中文** in General settings.

| Platform | Configuration directory |
| --- | --- |
| Windows | `%APPDATA%\OmniLyrics` |
| macOS | `~/Library/Application Support/OmniLyrics` |
| Linux | `~/.config/omnilyrics` |

Preferences are stored in `config.json`; Cider credentials use a separate `cider-token` file.
**Advanced → Edit configuration file** opens the JSON editor. Valid external edits also
update the settings and lyric window automatically; invalid edits leave the last valid settings in use.
`OMNILYRICS_CONFIG_DIR` overrides the directory, and `OMNILYRICS_LANGUAGE` overrides the saved language for a process.

## Cider V3+ support and configuration

Cider V3+ is the integration target; V2 and V1 are not supported. Favorites use the V4 library API.
Choose the playback connection in **Settings → Player connections → Cider**:

| Mode | Behavior |
| --- | --- |
| `auto` | Prefer an available Cider Web API, then try the system connection. |
| `webapi` | Use the Cider Web API; recommended for V3's incomplete MPRIS support. |
| `mpris` | On Linux, prefer Cider MPRIS when it offers the required controls; otherwise use the Web API. |

The CLI equivalent is `OmniLyrics.Cli config cider integration webapi` (or `auto` / `mpris`).
Favorites use the Web API regardless of the playback connection.

### With authentication

In Cider, open **Settings → Connectivity → Manage External Application Access** and create
an application token. Grant `playback` access, plus `library` for favorites.
Enter it in OmniLyrics' Cider settings or use the hidden terminal prompt:

```bash
OmniLyrics.Cli config cider token
OmniLyrics.Cli config cider test
```

A blank token field retains the saved token. `config show` does not display credentials.
For automation, `config cider token --stdin` reads the token from standard input.

### Without authentication

Turn off **Require API Tokens** in Cider, then choose **No token** in OmniLyrics or run:

```bash
OmniLyrics.Cli config cider none
```

The API, including V4 favorites, then works without a token. This mode ignores saved token files.
OmniLyrics does not change Cider's authentication switch for you.
On Linux, MPRIS playback can work without a token even when Cider's API authentication is enabled;
favorites still follow the API's authentication setting.

### Environment overrides

`CIDER_AUTH_MODE=token` or `none` overrides the saved mode. In token mode, credentials are read
from `CIDER_API_TOKEN`, then `CIDER_TOKEN_FILE`, then the saved `cider-token` file, in that order.
No-token mode ignores all token sources. Relaunch the application after changing its environment.

## Shared service and LAN access

GUI, TUI and CLI (including JSON widgets) reuse an existing OmniLyrics service.
When no service is running, local instances take over in **GUI → TUI → CLI** order.
A healthy service keeps running when another frontend opens; if it exits, a remaining instance takes over.
This coordination applies to instances using the same configuration directory and port pair.
If another application occupies the ports, OmniLyrics waits and retries.

The default is local-only: HTTP `127.0.0.1:27270`, UDP port `32651`.
To let other devices on a trusted LAN connect:

```bash
OmniLyrics.Cli config server lan        # Listen on all IPv4 interfaces
# Restart the running service after changing its listening settings.
OmniLyrics.Cli --mode line
# On another device, set the server's actual LAN address:
OmniLyrics.Cli config server target 192.168.1.50
# Restore local-only settings:
OmniLyrics.Cli config server local
OmniLyrics.Cli config server target 127.0.0.1
```

Use `config server listen ADDRESS` for a specific interface and `config server ports HTTP_PORT UDP_PORT`
for custom ports. A remote target is followed only; if it is offline, the client waits to reconnect.

For one run, `--listen`, `--host`, `--http-port` and `--udp-port` override the saved settings.
The corresponding environment variables are `OMNILYRICS_LISTEN_ADDRESS`, `OMNILYRICS_CONTROL_HOST`,
`OMNILYRICS_HTTP_PORT` and `OMNILYRICS_UDP_PORT`; command-line options take precedence.
These HTTP/UDP interfaces have no authentication and are intended for trusted networks.
A Cider token does not protect OmniLyrics' own service.

## Web API Endpoints

Use the service address, by default `http://127.0.0.1:27270`.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/snapshot` | Player state and lyrics for the same track, including word timing and translations when available |
| GET | `/playback/state` | Song metadata, player name, playback position and duration |
| GET | `/lyrics` | Parsed lyrics, or `null` when unavailable; 404 if no player state is available |
| POST | `/playback/play`, `/playback/pause`, `/playback/toggle` | Playback controls |
| POST | `/playback/next`, `/playback/prev` | Track navigation |
| POST | `/playback/seek` | Seek with a JSON body such as `{"position":42.5}` (seconds) |
| GET | `/favorites` | Current favorite state, or 404 when unsupported |
| POST | `/favorites` | Set favorite state using the previous GET response |

`/snapshot` includes `service`, `protocolVersion`, `state`, `lyrics`, `loading` and `lyricsVersion`.
For favorites, GET returns `{"trackId":"…","mediaKey":"…","isFavorite":false}`.
POST takes `{"previous":<the GET response>,"favorite":true}` and returns the confirmed state.
A stale track or unconfirmed update returns 409; unsupported connections return 404.

```bash
curl http://127.0.0.1:27270/snapshot
curl -X POST http://127.0.0.1:27270/playback/seek \
  -H 'Content-Type: application/json' -d '{"position":42.5}'
OmniLyrics.Cli --control pause
```

For desktop widgets, `--mode json` includes current/next lyrics and `currentTranslation` / `nextTranslation`.
See the [Quickshell integration guide](../integrations/quickshell/README.md) for installation.
