namespace OmniLyrics.Core.Lyrics.Models;

public record LyricsToken(
    TimeSpan StartTime,
    TimeSpan Duration,
    string Text
);

public record LyricsLine(
    TimeSpan Timestamp,
    string Text,
    List<LyricsToken>? Tokens,
    Dictionary<string, string>? Translations = null,
    TimeSpan? EndTime = null
)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Translation => Translations?.GetValueOrDefault("zh")
        ?? Translations?.GetValueOrDefault("en") ?? Translations?.Values.FirstOrDefault();
}
