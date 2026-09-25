#ifndef MyAppVersion
  #error "Use package_win64.ps1 to supply the project version."
#endif

[Setup]
AppName=OmniLyrics
AppVersion={#MyAppVersion}
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
