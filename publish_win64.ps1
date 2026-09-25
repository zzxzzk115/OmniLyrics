dotnet restore src/OmniLyrics.Cli
dotnet publish src/OmniLyrics.Cli -c Release -r win-x64 -o publish/win-x64/cli

dotnet restore src/OmniLyrics.Gui
dotnet publish src/OmniLyrics.Gui -c Release -r win-x64 -o publish/win-x64/gui