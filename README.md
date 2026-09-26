<p align="center">
  <img src="./src/OmniLyrics.Gui/Assets/logo.png" alt="OmniLyrics" width="96" height="96">
</p>

# OmniLyrics

English | [简体中文](./README.zh-CN.md)

OmniLyrics: A personal attempt to build the lyric tool I always wanted -- CLI, TUI, GUI, and cross-platform.

Five GUI layouts, bilingual karaoke lyrics, shared preferences and queue caching are available.
**0.4.1** adds single-file builds, native macOS playback, more player favorites and secure LAN pairing.
See the [user guide](./docs/user-guide.md) for configuration and integrations.

## Showcase

Current GUI (Focus layout):

![GUI Focus](./media/images/gui_preset_focus.png)

Earlier Windows GUI (Top: Cider Mini Player, Bottom: OmniLyrics):

![GUI Windows](./media/images/gui_windows.png)

Windows Terminal (Default Mode):

![CLI (Windows Terminal)](./media/images/cli_windows_terminal.png)

macOS Terminal (Default Mode):

![CLI (macOS Terminal)](./media/images/cli_macos_terminal.png)

Linux Waybar (Line Mode, --mode line):

![CLI (Linux Waybar)](./media/images/cli_linux_waybar.jpg)

## Build Instruction

Download and install [.NET 10 LTS SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) to build from source.
The 0.4.1 portable builds package the CLI and GUI as separate single executables with the .NET runtime included; no separate .NET installation is needed to run them.

> On macOS, Apple Music and Spotify connect through the built-in Apple Events bridge. Allow the Automation permission when prompted; no Homebrew dependency is required. Cider uses its Web API.
>
> Settings → macOS environment checks access and offers an explicit Homebrew install button. Every startup prompts again if both native access and the fallback are unavailable.

> Other players can optionally use `media-control`:
> ```bash
> brew install media-control
> ```

macOS GUI builds also include an `OmniLyrics.app.zip` artifact for Finder, alongside the portable executable. The application menu, Dock name and About item use OmniLyrics. The app bundle is ad-hoc signed, not Developer ID notarized. To package a local publish:

```bash
bash build/macos/package.sh /path/to/OmniLyrics.Gui /path/to/package-output 0.4.1
```

Favorites support Apple Music (native on macOS), Spotify (configurable browser authorization), YesPlayMusic (QR authorization), and Cider. See [account setup](./docs/user-guide.md#favorites-and-account-access). Tray shortcuts include an emergency 100% scale reset and settings-window recovery.

Clone:

```bash
git clone https://github.com/zzxzzk115/OmniLyrics.git
```

Build and Run:

```bash
cd OmniLyrics

# Build the entire solution
dotnet build

# Launch the GUI
dotnet run --project src/OmniLyrics.Gui

# Launch the CLI
dotnet run --project src/OmniLyrics.Cli

# Launch the CLI in single-line output mode
# (Suitable for status bars)
dotnet run --project src/OmniLyrics.Cli -- --mode line

# Stream JSON for desktop widgets, including bilingual lyrics
dotnet run --project src/OmniLyrics.Cli -- --mode json

# -------------------------------------------------------------------
# Remote control commands
# Keep an OmniLyrics instance running on the source device.
# A selected paired source requires playback-control permission.
# -------------------------------------------------------------------

# Playback control
dotnet run --project src/OmniLyrics.Cli -- --control play
dotnet run --project src/OmniLyrics.Cli -- --control pause
dotnet run --project src/OmniLyrics.Cli -- --control toggle

# Track navigation
dotnet run --project src/OmniLyrics.Cli -- --control prev
dotnet run --project src/OmniLyrics.Cli -- --control next

# Seek to a position (in seconds)
dotnet run --project src/OmniLyrics.Cli -- --control seek 10
```

### Waybar Module Config

```json
// OmniLyrics
"custom/OmniLyrics": {
  "format": "  {text}",
  "exec": "/path/to/OmniLyrics.Cli --mode line",
  "return-type": "text",
  "escape": true
},
```

---

## Lyrics across LAN devices

Display another computer's lyrics, including word timing and translations when available. Keep OmniLyrics running on both devices:

1. On the source device, open **Settings → LAN devices**, enable sharing and save, then create an invitation using its LAN address.
2. On the receiving device, use **Find nearby devices** to discover available sources, then paste the source's private invitation into **Pair device**. An invitation also works when discovery is blocked.
3. Select the paired device and choose **Show this device’s lyrics**. Choose **Use this device’s player** to switch back.

Sharing is off by default. Invitations expire after five minutes and can be used once; share them privately. Pairing grants read-only lyrics access unless **Also allow playback and favorites control** was checked when creating the invitation. Access can be revoked on the source device at any time.

OmniLyrics apps on the same computer communicate without pairing or tokens through loopback HTTP/UDP. Other devices use authenticated HTTPS with certificate verification. Default LAN ports are TCP `27271` for HTTPS and UDP `32652` for discovery. CLI and interactive terminal setup use `OmniLyrics.Cli config lan`.

See [shared service and LAN setup](./docs/user-guide.md#shared-service-and-lan-access) for commands, firewall settings and device management. For a local desktop widget, see the [Quickshell integration guide](./integrations/quickshell/README.md).

## Web API Endpoints

GUI, TUI and CLI (including JSON mode) share the local HTTP service at `http://127.0.0.1:27270` without authentication. When its owner exits, remaining local instances take over in GUI → TUI → CLI order.
The endpoints below also serve paired LAN clients over HTTPS; playback controls and favorites require control permission. See the [protocol guide](./docs/user-guide.md#web-api-endpoints) for snapshots, favorites and request details.

### Lyrics API

#### **GET /snapshot**

Returns player state and matching lyrics together, including word timing, translations, loading status and a lyrics revision. Recommended for clients displaying synchronized lyrics.

#### **GET /lyrics**

Returns the current track's parsed LRC lyrics as JSON.

**Response**

```json
[
  { "timestamp": "00:00:12.4500000", "text": "We're no strangers to love" },
  { "timestamp": "00:00:16.8000000", "text": "You know the rules and so do I" }
]
```

If a track is available but its lyrics have not loaded:

```json
null
```

Returns `404 Not Found` when no current player state is available.

---

### Playback Control API

All control endpoints return `200 OK` on success.

#### **POST /playback/play**

Starts playback.

#### **POST /playback/pause**

Pauses playback.

#### **POST /playback/toggle**

Toggles play/pause.

#### **POST /playback/next**

Skips to the next track.

#### **POST /playback/prev**

Skips to the previous track.

#### **POST /playback/seek**

Seek to a given position.

**Body:**

```json
{ "position": 42.5 }
```

(seconds)

---

### Metadata API

#### **GET /playback/state**

Returns the current player state as JSON:

```json
{
  "title": "Song Title",
  "artists": ["Artist A", "Artist B"],
  "album": "Best Album",
  "position": "00:00:12.3400000",
  "duration": "00:03:00",
  "playing": true,
  "sourceApp": "Cider",
  "artworkUrl": "https://example.com/art.jpg",
  "artworkWidth": 640,
  "artworkHeight": 640
}
```

---

## Cider V3+ Settings

Cider V4 is the current commercial release; V3 remains a compatibility target. V2 and V1 are not supported.

- **With authentication:** create an application token in Cider's Settings → Connectivity → Manage External Application Access, then enter it in OmniLyrics Settings → Player connections, or run `config cider token` in the CLI.
- **Without authentication:** disable **Require API Tokens** in Cider, then choose **No token** in OmniLyrics or run `config cider none`. The API, including favorites where supported, works without a token in this mode.

GUI, interactive terminal and CLI share these settings. Blank token input retains the saved token. See [connection details](./docs/user-guide.md#cider-v3-support-and-configuration).

## TODO List

Common Backends:

- [x] SMTC for Windows
- [x] MPRIS for Linux
- [x] Optional media-control fallback for macOS
- [x] Native Apple Events for Apple Music / Spotify on macOS

Software-specific Backends:

- [x] [Cider V3+](https://cider.sh/) (V4 tested; V3 compatibility target)
- [x] [YesPlayMusic](https://github.com/qier222/YesPlayMusic)

Server & API

- [x] Local UDP control (127.0.0.1:32651)
- [x] Local Web API (http://127.0.0.1:27270)
- [x] LAN discovery, authenticated HTTPS pairing and device revocation

CLI:

- [x] Multiple Line Mode (Default)
- [x] Single Line Mode (for Waybar)
- [x] Local UDP control and authorized HTTPS control of paired devices

TUI:

- [x] Interactive shared configuration, including LAN pairing

GUI:

- [x] Five layouts, bilingual karaoke, window lock and optional favorites
- [x] Searchable settings, appearance controls and Chinese/English UI
- [x] Lyrics source selection from paired LAN devices

## Acknowledgement

- [Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)
- [WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController)
- [Tmds.DBus](https://github.com/tmds/Tmds.DBus)
- [media-control](https://github.com/ungive/media-control)

## License

This project is under the [MIT](./LICENSE) License.
