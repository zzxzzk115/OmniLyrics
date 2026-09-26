# User guide

English | [简体中文](./user-guide.zh-CN.md) · [README](../README.md)

For installation and build instructions, see the [README](../README.md#build-instruction).
Open the lyric window's gear button or the tray menu to access **Settings**.

## Players and platforms

| Platform | Connection | Players |
| --- | --- | --- |
| Windows | System Media Transport Controls (SMTC) | Spotify and other players exposing system media controls |
| macOS | Native Apple Events | Apple Music and Spotify desktop |
| macOS | Optional `media-control` | Other players exposing Now Playing |
| Linux | MPRIS | Spotify and other MPRIS players |
| All three | Player-specific API | Cider V3+ and YesPlayMusic |

On macOS, Apple Music and Spotify use public Apple Events through the system's `/usr/bin/osascript` (JavaScript for Automation). No extra installation is needed. Only running players are queried; OmniLyrics does not launch them. Allow the macOS Automation prompt for the application that launches OmniLyrics (for example, your terminal). If denied, enable the player under **System Settings → Privacy & Security → Automation**, then restart OmniLyrics. The lyric window shows an actionable message when native access fails and no other connection is available.

On macOS, **Settings → macOS environment** checks native access, Homebrew and media-control compatibility. Each CLI/GUI startup checks again. When both native access and media-control are unavailable, an interactive launch asks whether to install the fallback; declining does not suppress the next launch’s prompt. Line/JSON modes and redirected input/output print recovery instructions to stderr without waiting for input. Installation runs only after confirmation and shows Homebrew output; cancellation and failures remain visible. If Homebrew is missing, the page links to its official setup. A pending Automation request is not treated as a failed connection. Explicitly disabling the fallback with `OMNILYRICS_MEDIA_CONTROL=off` suppresses installation requests because installation cannot enable a disabled backend.

Apple Events are not exclusive to recent macOS releases: the JXA bridge used here dates to [OS X 10.10](https://developer.apple.com/library/archive/documentation/LanguagesUtilities/Conceptual/MacAutomationScriptingGuide/). This .NET 10 application requires [macOS 14 or newer](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md); installing media-control does not make unsupported operating systems supported.

With macOS blur enabled, the lyric background layer automatically uses 0% opacity so it does not cover Avalonia's native blur material. Text receives local contrast protection and controls use small backing surfaces, leaving the rest of the toolbar transparent; low-contrast text colors are adjusted for rendering without changing the saved palette. Disabling blur restores the editable background opacity. Apple Events progress is interpolated continuously; small sampling errors are corrected gradually instead of stepping the karaoke highlight forward.

Cider continues to use its Web API. For other players, optionally install `media-control` with `brew install media-control`. OmniLyrics checks PATH and the standard Homebrew locations. Set `OMNILYRICS_MEDIA_CONTROL=off` to disable this fallback, or set it to the executable's absolute path. Apple Music / Spotify native connections and Cider's Web API take precedence over a duplicate system-media connection. Missing or failed optional connections do not prevent other players from working.

Native Apple Music / Spotify support covers metadata, playback position, play/pause, previous/next and seek. Native queues are not integrated.

## Favorites and account access

| Player | Favorite connection | Setup |
| --- | --- | --- |
| Apple Music on macOS | Native Apple Events | Allow Automation access to Music; no extra account sign-in |
| Spotify desktop | Spotify Web API | Settings → Player connections → Spotify: enter your developer app Client ID and authorize in the browser |
| YesPlayMusic desktop | Bundled local NetEase API | Use the local API directly first; if it requires sign-in, scan the QR code with NetEase Cloud Music using the same account |
| Cider | V4 library API | Enable library permission when API token authentication is required |

Buttons appear only after the connected player reports a matching song and readable favorite state. Writes use explicit song IDs where supported, reject stale state after a track change, and wait for a confirmed readback. An unavailable or rejected API does not display a successful favorite. The shared `/favorites` endpoint uses these same adapters.

Spotify uses [Authorization Code with PKCE](https://developer.spotify.com/documentation/web-api/tutorials/code-pkce-flow); a Client Secret is not needed. Register **`http://127.0.0.1/spotify/callback`** without a port in the Spotify developer dashboard. A temporary loopback port is selected for each sign-in, as supported by Spotify's [redirect URI rules](https://developer.spotify.com/documentation/web-api/concepts/redirect_uri). Authorization requests only `user-library-read`, `user-library-modify`, and `user-read-currently-playing`. Spotify development-mode account restrictions still apply; check the [current developer requirements](https://developer.spotify.com/documentation/web-api/concepts/quota-modes). The authorized account's currently playing song must match the desktop player's song. Spotify favorites use the current `/me/library` endpoints; they do not require Spotify's desktop scripting interface to expose library controls.

YesPlayMusic first uses any session exposed by its local Web API, without requiring a manually entered token or saved cookie. Ports 27232 and 10754 must be available; use **Check local API** in settings to test access. The official proxy does not attach the player's browser cookies for other clients. If the API reports no signed-in session, QR login can authorize OmniLyrics separately. Cancelling or closing settings stops sign-in. **Remove saved authorization** clears OmniLyrics' local authorization; it does not sign the player out. Spotify refresh tokens and the NetEase session cookie are kept in separate private files in the configuration directory (owner-only permissions on macOS/Linux), excluded from exported settings. Do not share these credential files.

## Layouts and appearance

In **Settings → General → Interface scale**, the default follows the current monitor. Choose 100–200% or a custom 75–300% scale to override it for all windows, immediately. This does not change your saved lyric font sizes. In the configuration file, `appearance.uiScale` is `null` for system scaling or a factor such as `1.5` for 150%.

The native tray menu provides scale, layout, dark/light theme, blur, translation and text-size shortcuts. If a large scale moves settings outside the screen, choose **Restore 100% scale and settings window** to reset scale and bring the complete settings window onto the monitor. This menu is independent of OmniLyrics' interface scale.

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

Local HTTP (`127.0.0.1:27270`) and UDP control (`32651`) need no authentication between apps on the same computer. These listeners are restricted to loopback; browser-origin API calls are rejected. Other LAN devices must pair over HTTPS.

In **Settings → LAN devices**, enable sharing on the source device and save. Generate an invitation using its LAN address, transfer it privately to the receiving device, then paste it in **Pair device**. Select the paired device and choose **Show this device’s lyrics**. Sharing is off by default; pairing grants read-only lyrics access unless **Also allow playback and favorites control** was checked when creating the invitation.

Invitations expire after five minutes and can be used once. They include the source certificate fingerprint, which is checked before credentials are sent; discovering a device does not make it trusted. **Cancel invitation** invalidates an unused invitation. **Revoke access** on the source immediately blocks subsequent requests from that device. **Forget remote device** only removes the receiving device’s saved connection.

The selected source applies to GUI, TUI and CLI using the same configuration directory, including CLI `--control` commands. Local service ownership still works independently and always exports local playback, avoiding relay loops. An offline or revoked source clears its lyrics; it does not silently switch playback controls to the local player. Choose **Use this device’s player** to switch back.

CLI and interactive terminal configuration use the same settings:

```bash
OmniLyrics.Cli config lan                        # Interactive setup
OmniLyrics.Cli config lan on                     # Enable sharing on the source
OmniLyrics.Cli config lan invite 192.168.1.50     # Private, read-only invitation
# Add --control to the invite command only to allow playback/favorites control.
OmniLyrics.Cli config lan discover               # Find sources on the local network
OmniLyrics.Cli config lan pair                   # Paste invitation into hidden input
# Use config lan pair --stdin to read one invitation from a pipe, not an argument.
OmniLyrics.Cli config lan peers                  # IDs and permissions, never credentials
OmniLyrics.Cli config lan source DEVICE_ID
OmniLyrics.Cli config lan source local
OmniLyrics.Cli config lan revoke ACCESS_ID        # Run on the source
OmniLyrics.Cli config lan off
```

Keep a current GUI, TUI or CLI running on the source. HTTPS uses TCP **27271**; discovery uses UDP **32652**. Allow these ports only on private networks in your firewall. Discovery uses IPv4 broadcast and may be blocked by guest Wi-Fi or VLANs; an invitation can connect directly without broadcast. Private IPv4 and IPv6 addresses are supported for direct pairing; Internet endpoints are not. `config lan ports HTTPS_PORT UDP_PORT` changes the LAN ports, and `config lan name NAME` sets the device name. Devices must use the same discovery port to find each other. Certificate pinning is retained when rediscovery finds a new IP address.

Pairing credentials and the device certificate are stored separately in `lan-trust.json` in the private configuration directory. Do not publish or copy that file to other devices. Cider/Spotify/NetEase credentials are never part of discovery or pairing. LAN API requests require the paired bearer token over HTTPS with the pinned certificate, including when using custom clients. Read-only grants cannot read or change favorites. There is no plaintext or certificate-verification bypass fallback.

`config server lan` is now an alias for enabling secure sharing. Old wildcard HTTP/UDP listeners are restricted to loopback; migrate remote `server target` settings to pairing. `config server listen ADDRESS` and `config server target HOST` accept loopback addresses only. `config server ports HTTP_PORT UDP_PORT` and the existing `--listen`, `--host`, `--http-port`, `--udp-port` / `OMNILYRICS_*` overrides configure local IPC only.

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
