# NMSL

No More Searching Lyrics. A personal attempt to build the lyric tool I always wanted -- CLI, TUI, GUI, and cross-platform.

## Showcase

Windows Terminal (Default Mode):

![CLI (Windows Terminal)](./media/images/cli_windows_terminal.png)

Linux Waybar (Line Mode, --mode line):

![CLI (Linux Waybar)](./media/images/cli_linux_waybar.jpg)


## Build Instruction
Install .NET 8.0

Clone:
```bash
git clone https://github.com/zzxzzk115/NMSL.git
```

Build and Run:
```bash
cd NMSL
dotnet build

# Run Windows CLI
dotnet run --project src/NMSL.Cli.Windows

# Run Linux CLI
dotnet run --project src/NMSL.Cli.Linux

# Run Linux CLI in line mode for waybar
dotnet run --project src/NMSL.Cli.Linux --mode line
```

### Waybar Module Config

```
// NMSL
"custom/NMSL": {
  "format": "  {text}",
  "exec": "/path/to/NMSL.Cli.Linux --mode line",
  "return-type": "text",
  "escape": true
},
```

## TODO List
Common Backends:
- [x] SMTC for Windows
- [x] MPRIS for Linux
- [ ] NowPlaying for macOS

Software-specific Backends:
- [ ] YesPlayMusic
- [ ] Cider

CLI:
- [x] Multiple Line Mode (Default)
- [x] Line Mode (for Waybar)

TUI:

GUI:

## Acknowledgement
- [Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)

## License

This project is under the [MIT](./LICENSE) License.