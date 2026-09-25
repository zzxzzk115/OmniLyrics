# OmniLyrics

English | [简体中文](./README.zh-CN.md)

OmniLyrics: A personal attempt to build the lyric tool I always wanted -- CLI, TUI, GUI, and cross-platform.

Version **0.4.0** adds five GUI layouts, bilingual karaoke lyrics, shared preferences and queue caching.
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

Download and Install [.NET 10 LTS SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

> On macOS, you need to install `media-control`:
> ```bash
> brew install media-control
> ```

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

# -------------------------------------------------------------------
# Remote control commands
# These commands require an Omnilyrics instance running in lyrics/daemon mode.
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

## Web API Endpoints

Default and line modes provide an HTTP service at `http://127.0.0.1:27270`.
GUI, TUI and CLI reuse an existing service; when it stops, remaining local instances take over in GUI → TUI → CLI order. For snapshots, favorites and trusted-LAN configuration, see the [protocol guide](./docs/user-guide.md#web-api-endpoints).

### Lyrics API

#### **GET /lyrics**

Returns the current track's parsed LRC lyrics as JSON.

**Response**

```json
[
  { "timestamp": "00:00:12.4500000", "text": "We're no strangers to love" },
  { "timestamp": "00:00:16.8000000", "text": "You know the rules and so do I" }
]
```

If no lyrics are available:

```json
null
```

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
- [x] media-control for macOS

Software-specific Backends:

- [x] [Cider V3+](https://cider.sh/) (V4 tested; V3 compatibility target)
- [x] [YesPlayMusic](https://github.com/qier222/YesPlayMusic)

Server & API

- [x] UDP Client & Server (localhost:32651)
- [x] Web API (http://localhost:27270)

CLI:

- [x] Multiple Line Mode (Default)
- [x] Single Line Mode (for Waybar)
- [x] Remote Control (through UDP commands)

TUI:

- [x] Interactive shared configuration

GUI:

- [x] Five layouts, bilingual karaoke, window lock and optional favorites
- [x] Searchable settings, appearance controls and Chinese/English UI

## Acknowledgement

- [Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)
- [WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController)
- [Tmds.DBus](https://github.com/tmds/Tmds.DBus)
- [media-control](https://github.com/ungive/media-control)

## License

This project is under the [MIT](./LICENSE) License.
