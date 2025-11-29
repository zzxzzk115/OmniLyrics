# OmniLyrics

OmniLyrics: A personal attempt to build the lyric tool I always wanted -- CLI, TUI, GUI, and cross-platform.

## Showcase

Windows Terminal (Default Mode):

![CLI (Windows Terminal)](./media/images/cli_windows_terminal.png)

macOS Terminal (Default Mode):

![CLI (macOS Terminal)](./media/images/cli_macos_terminal.png)

Linux Waybar (Line Mode, --mode line):

![CLI (Linux Waybar)](./media/images/cli_linux_waybar.jpg)


## Build Instruction
Download and Install [.NET SDK 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

Clone:
```bash
git clone https://github.com/zzxzzk115/OmniLyrics.git
```

Build and Run:
```bash
cd OmniLyrics

# Build
dotnet build

# Run Windows-only CLI
dotnet run --project src/OmniLyrics.Cli.Windows

# Run Non-Windows CLI
dotnet run --project src/OmniLyrics.Cli

# Run Non-Windows CLI in Single Line mode (can be used for status bars)
dotnet run --project src/OmniLyrics.Cli --mode line
```

### Waybar Module Config

```
// OmniLyrics
"custom/OmniLyrics": {
  "format": "  {text}",
  "exec": "/path/to/OmniLyrics.Cli.Linux --mode line",
  "return-type": "text",
  "escape": true
},
```

## Cider V3 Settings

Settings -> Connectivity -> Manage External Application Access to Cider -> Disable "Require API Tokens"

> Currently, we don't have custom token support.

## TODO List
Common Backends:
- [x] SMTC for Windows
- [x] MPRIS for Linux
- [x] media-control for macOS

Software-specific Backends:
- [x] Cider v3 (Commercial Version)
- [ ] Cider v2 (Open Source Version)
- [ ] YesPlayMusic

CLI:
- [x] Multiple Line Mode (Default)
- [x] Single Line Mode (for Waybar)

TUI:

GUI:

## Acknowledgement
- [Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)
- [WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController)
- [Tmds.DBus](https://github.com/tmds/Tmds.DBus)
- [media-control](https://github.com/ungive/media-control)

## License

This project is under the [MIT](./LICENSE) License.