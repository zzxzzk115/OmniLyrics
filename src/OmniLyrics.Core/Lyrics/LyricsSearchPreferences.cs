using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Core.Lyrics;

/// <summary>Frontends share search preferences without reading a file on every animation frame.</summary>
public static class LyricsSearchPreferences
{
    private static readonly object Gate = new();
    private static LyricsSettings _current = new(true, 5);
    private static long _nextRead;
    private static string? _directory;

    public static LyricsSettings Read()
    {
        lock (Gate)
        {
            var directory = UserConfiguration.DirectoryPath;
            if (_directory == directory && Environment.TickCount64 < _nextRead) return _current;
            if (_directory != directory) _current = new(true, 5);
            _directory = directory;
            _nextRead = Environment.TickCount64 + 300;
            try { _current = UserConfiguration.LoadLyrics(); }
            catch { /* An incomplete external edit keeps the last valid policy. */ }
            return _current;
        }
    }

    // Prefetch is deliberately excluded: changing its queue size must not invalidate lyrics.
    public static string Key(LyricsSettings settings) =>
        $"search-v1|{settings.SearchStrategy}|{settings.MatchMode}|{settings.PlayerSource}|{settings.QQMusicSource}|{settings.NeteaseSource}|{settings.PreferredSource}";

    public static IEnumerable<string> Sources(LyricsSettings settings)
    {
        if (settings.PlayerSource) yield return "player";
        if (settings.PreferredSource == "netease" && settings.NeteaseSource) yield return "netease";
        if (settings.QQMusicSource) yield return "qq";
        if (settings.PreferredSource != "netease" && settings.NeteaseSource) yield return "netease";
    }
}
