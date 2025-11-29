using OmniLyrics.Backends.Factory;
using OmniLyrics.Core.Cli;

await LyricsCliRunner.RunAsync(BackendFactory.Create(), args);