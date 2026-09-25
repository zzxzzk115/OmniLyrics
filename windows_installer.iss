[Setup]
AppName=OmniLyrics
AppVersion=0.3.0
DefaultDirName={commonpf}\OmniLyrics
DefaultGroupName=OmniLyrics
OutputBaseFilename=OmniLyrics-Setup
Compression=lzma
SolidCompression=yes

[Files]
Source: "publish\win-x64\gui\*"; DestDir: "{app}"; Flags: recursesubdirs
Source: "publish\win-x64\cli\*"; DestDir: "{app}\cli"; Flags: recursesubdirs

[Icons]
Name: "{group}\OmniLyrics GUI"; Filename: "{app}\OmniLyrics.Gui.exe"
Name: "{group}\OmniLyrics CLI"; Filename: "{app}\cli\OmniLyrics.Cli.exe"
