namespace OmniLyrics.Core.Lyrics.Models;

public record LyricsToken(
    TimeSpan StartTime,
    TimeSpan Duration,
    string Text
);

public record LyricsLine (
    TimeSpan Timestamp,
    string Text,
    List<LyricsToken>? Tokens
);