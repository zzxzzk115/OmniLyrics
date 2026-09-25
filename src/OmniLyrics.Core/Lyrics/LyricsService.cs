using System.Net;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;
using OmniLyrics.Core.Helpers;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Core.Lyrics;

public class LyricsService
{
    static LyricsService()
    {
        Lyricify.Lyrics.Providers.Web.BaseApi.HttpClient.Timeout = TimeSpan.FromSeconds(8);
    }
    private readonly Lyricify.Lyrics.Providers.Web.Netease.Api _neteaseApi = new();
    private readonly Lyricify.Lyrics.Providers.Web.QQMusic.Api _qqMusicApi = new();

    private readonly YesPlayMusicApi _yesPlayMusicsLyricsApi = new();

    private readonly IReadOnlyList<Func<PlayerState, bool, Task<List<LyricsLine>?>>>? _sources;
    private readonly IReadOnlyDictionary<string, Func<PlayerState, bool, LyricsSettings, Task<List<LyricsLine>?>>> _providers;

    public LyricsService(IReadOnlyList<Func<PlayerState, bool, Task<List<LyricsLine>?>>>? sources = null)
    {
        _sources = sources;
        _providers = new Dictionary<string, Func<PlayerState, bool, LyricsSettings, Task<List<LyricsLine>?>>>
        {
            ["player"] = (state, karaoke, _) => PlayerLyricsAsync(state, karaoke),
            ["qq"] = QQAsync,
            ["netease"] = NeteaseAsync
        };
    }

    public LyricsService(IReadOnlyDictionary<string, Func<PlayerState, bool, LyricsSettings, Task<List<LyricsLine>?>>> providers)
        => _providers = providers;

    public static bool HasWordTiming(List<LyricsLine> lines) => lines.Any(line =>
        line.Tokens?.Any(word => word.Duration > TimeSpan.Zero && !string.IsNullOrWhiteSpace(word.Text)) == true);

    public Task<List<LyricsLine>?> SearchLyricLinesAsync(PlayerState state, bool karaoke)
        => SearchLyricLinesAsync(state, karaoke, LyricsSearchPreferences.Read());

    public async Task<List<LyricsLine>?> SearchLyricLinesAsync(PlayerState state, bool karaoke, LyricsSettings settings)
    {
        List<LyricsLine>? fallback = null;
        var sources = _sources ?? LyricsSearchPreferences.Sources(settings).Where(_providers.ContainsKey)
            .Select(id => (Func<PlayerState, bool, Task<List<LyricsLine>?>>)((track, timed) => _providers[id](track, timed, settings))).ToList();
        foreach (var source in sources)
        {
            try
            {
                var result = await source(state.DeepCopy(), karaoke);
                if (result is not { Count: > 0 }) continue;
                if (!karaoke || HasWordTiming(result)) return result;
                fallback ??= result;
            }
            catch { /* One unavailable source must not prevent another source from succeeding. */ }
        }
        return fallback;
    }

    private async Task<List<LyricsLine>?> PlayerLyricsAsync(PlayerState state, bool karaoke)
    {
        if (state.SourceApp?.Contains("yesplaymusic", StringComparison.OrdinalIgnoreCase) != true) return null;
        return await _yesPlayMusicsLyricsApi.TryGetLyricLinesAsync(state);
    }

    private static TrackMultiArtistMetadata Metadata(PlayerState state)
    {
        ArtistHelper.ChineselizeArtists(state.Artists);
        return new TrackMultiArtistMetadata
        {
            Title = state.Title, Artists = state.Artists, AlbumArtists = state.Artists,
            Album = state.Album, DurationMs = (int)state.Duration.TotalMilliseconds
        };
    }

    private async Task<List<LyricsLine>?> QQAsync(PlayerState state, bool karaoke, LyricsSettings settings)
    {
        var song = await FindMatchAsync(state, new QQMusicSearcher(), settings) as QQMusicSearchResult;
        if (song == null) return null;
        List<LyricsLine>? fallback = null;
        if (karaoke)
        {
            try
            {
                var response = await _qqMusicApi.GetLyricsAsync(song.Id);
                var raw = response?.Lyrics;
                var parsed = string.IsNullOrWhiteSpace(raw) ? null : AttachTranslation(ParseLyrics(raw, LyricsRawTypes.Unknown), response?.Trans);
                if (parsed is { Count: > 0 })
                {
                    if (HasWordTiming(parsed)) return parsed;
                    fallback = parsed;
                }
            }
            catch { /* Keep looking for line lyrics; another source may still have word timing. */ }
        }
        if (fallback != null) return fallback;
        var plain = await _qqMusicApi.GetLyric(song.Mid);
        return string.IsNullOrWhiteSpace(plain?.Lyric) ? null : AttachTranslation(ParseLyrics(plain.Lyric, LyricsRawTypes.Lrc), plain.Trans);
    }

    private async Task<List<LyricsLine>?> NeteaseAsync(PlayerState state, bool karaoke, LyricsSettings settings)
    {
        var song = await FindMatchAsync(state, new NeteaseSearcher(), settings) as NeteaseSearchResult;
        if (song == null) return null;
        var lyrics = await _neteaseApi.GetLyricNew(song.Id);
        if (karaoke && !string.IsNullOrWhiteSpace(lyrics?.Yrc?.Lyric))
        {
            try
            {
                var parsed = AttachTranslation(ParseLyrics(lyrics.Yrc.Lyric, LyricsRawTypes.Yrc),
                    !string.IsNullOrWhiteSpace(lyrics.Ytlrc?.Lyric) ? lyrics.Ytlrc.Lyric : lyrics.Tlyric?.Lyric);
                if (parsed is { Count: > 0 } && HasWordTiming(parsed)) return parsed;
            }
            catch { /* A malformed YRC must not hide an available LRC. */ }
        }
        var plain = lyrics?.Lrc?.Lyric;
        return string.IsNullOrWhiteSpace(plain) ? null : AttachTranslation(ParseLyrics(plain, LyricsRawTypes.Lrc), lyrics?.Tlyric?.Lyric);
    }

    internal static async Task<ISearchResult?> FindMatchAsync(PlayerState state, Searcher searcher, LyricsSettings settings)
    {
        var metadata = Metadata(state);
        foreach (var fullSearch in settings.SearchStrategy == "quick" ? new[] { false } : new[] { false, true })
        {
            var candidates = await searcher.SearchForResults(metadata, fullSearch);
            var match = candidates.Where(candidate => TrackMatch.IsSuitable(state, candidate, settings.MatchMode))
                .OrderByDescending(candidate => candidate.MatchType).FirstOrDefault();
            if (match != null) return match;
        }
        return null;
    }

    public static List<LyricsLine>? ParseLyrics(string lrc, LyricsRawTypes type)
    {
        var lyricsData = type == LyricsRawTypes.Unknown ? ParseHelper.ParseLyrics(lrc) : ParseHelper.ParseLyrics(lrc, type);
        // The released Helper detector can return Unknown for short, valid LRC
        // fragments (especially translations). A timestamp signature is enough
        // to safely try its LRC parser without guessing line order.
        if (lyricsData == null && type == LyricsRawTypes.Unknown
            && System.Text.RegularExpressions.Regex.IsMatch(lrc, @"\[\d+:\d{2}(?:[.:]\d+)?\]"))
            lyricsData = ParseHelper.ParseLyrics(lrc, LyricsRawTypes.Lrc);
        if (lyricsData == null || lyricsData.Lines == null)
            return null;

        var result = new List<LyricsLine>();
        foreach (var line in lyricsData.Lines)
        {
            if (line.StartTime == null || string.IsNullOrWhiteSpace(line.Text))
                continue;

            // Decode HTML entities to normal characters
            string decoded = WebUtility.HtmlDecode(line.Text);

            // If it's Karaoke lyrics, record tokens
            var tokens = new List<LyricsToken>();
            if (line is SyllableLineInfo)
            {
                var syllableLine = line as SyllableLineInfo;
                foreach (var syllable in syllableLine!.Syllables)
                {
                    tokens.Add(new LyricsToken(
                        TimeSpan.FromMilliseconds(syllable.StartTime),
                        TimeSpan.FromMilliseconds(Math.Max(0, syllable.EndTime - syllable.StartTime)),
                        WebUtility.HtmlDecode(syllable.Text)));
                }
            }

            var translations = line is IFullLineInfo full && full.Translations.Count > 0
                ? full.Translations.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                    .ToDictionary(pair => pair.Key, pair => WebUtility.HtmlDecode(pair.Value)) : null;
            result.Add(new LyricsLine(TimeSpan.FromMilliseconds((long)line.StartTime), decoded, tokens.Count > 0 ? tokens : null,
                translations, line.EndTime.HasValue ? TimeSpan.FromMilliseconds(line.EndTime.Value) : null));
        }

        return result.OrderBy(line => line.Timestamp).ToList();
    }

    public static List<LyricsLine>? AttachTranslation(List<LyricsLine>? original, string? raw, string language = "zh")
    {
        if (original == null || string.IsNullOrWhiteSpace(raw)) return original;
        try
        {
            var translations = ParseLyrics(raw, LyricsRawTypes.Unknown);
            if (translations == null) return original;
            var used = new HashSet<int>();
            return original.Select(line =>
            {
                var candidates = translations.Select((value, index) => (value, index,
                    distance: Math.Abs((value.Timestamp - line.Timestamp).TotalMilliseconds)))
                    .Where(item => item.distance <= 350 && !used.Contains(item.index)).OrderBy(item => item.distance).ToList();
                if (candidates.Count == 0 || candidates.Count > 1 && candidates[0].distance == candidates[1].distance) return line;
                var match = candidates[0];
                // Missing lines must not shift the translation stream.
                if (original.Any(other => Math.Abs((other.Timestamp - match.value.Timestamp).TotalMilliseconds) < match.distance)) return line;
                used.Add(match.index);
                if (match.value.Text.Trim() == line.Text.Trim()) return line;
                var text = new Dictionary<string, string>(line.Translations ?? []);
                text.TryAdd(language, match.value.Text);
                return line with { Translations = text };
            }).ToList();
        }
        catch { return original; }
    }
}
