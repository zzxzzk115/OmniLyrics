using OmniLyrics.Core.Lyrics;
using OmniLyrics.Core.Lyrics.Models;

namespace OmniLyrics.Core.Shared;

public class LyricsManager
{
    private readonly Dictionary<string, List<LyricsLine>?> _cache = new();
    private readonly LyricsService _lyricsService = new();

    private string _lastId = "";

    public List<LyricsLine>? Current { get; private set; }

    public async Task UpdateAsync(PlayerState? state, bool karaoke)
    {
        if (state == null)
            return;

        string normTitle = (state.Title ?? "").Trim().ToLowerInvariant();
        string normArtist = (state.Artists.FirstOrDefault() ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(normTitle) || string.IsNullOrEmpty(normArtist))
            return;

        string id = $"{state.SourceApp}|{normTitle}|{normArtist}";

        // no change
        if (id == _lastId)
            return;

        _lastId = id;
        Current = null;

        // cache hit
        if (_cache.TryGetValue(id, out var cached))
        {
            Current = cached;
            return;
        }

        // load new (no karaoke)
        var parsed = await _lyricsService.SearchLyricLinesAsync(state, karaoke);
        _cache[id] = parsed;
        Current = parsed;
    }
}