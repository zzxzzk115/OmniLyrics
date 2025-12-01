using System.Net;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Providers.Web.QQMusic;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Helpers;

namespace OmniLyrics.Core.Lyrics;

public class LyricsService
{
    private readonly Api _api = new();

    private readonly YesPlayMusicApi _yesPlayMusicsLyricsApi = new();

    public async Task<List<LyricsLine>?> SearchLyricLinesAsync(PlayerState state, bool karaoke)
    {
        try
        {
            // Try get lyrics from YesPlayMusic first
            var app = state.SourceApp ?? "";
            if (app.Contains("yesplaymusic", StringComparison.OrdinalIgnoreCase))
            {
                // Use embeded lyrics (LRC)
                if (!karaoke)
                {
                    var embededLyrics = await _yesPlayMusicsLyricsApi.TryGetLyricsAsync();
                    if (embededLyrics != null)
                    {
                        // YesPlayMusic only provides LRC format
                        return ParseLyrics(embededLyrics, LyricsRawTypes.Lrc);
                    }
                }
                // TODO: Use Netease API
                else { }
            }

            var song = await SearchSongAsync(state);
            if (song == null)
                return null;

            if (karaoke)
            {
                var lyrics = await _api.GetLyricsAsync(song.Id);
                if (lyrics == null)
                    return null;

                return ParseLyrics(lyrics.Lyrics!, LyricsRawTypes.Qrc);
            }
            else
            {
                var lyrics = await _api.GetLyric(song.Mid);
                if (lyrics == null)
                    return null;

                return ParseLyrics(lyrics.Lyric, LyricsRawTypes.Lrc);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<QQMusicSearchResult?> SearchSongAsync(PlayerState state)
    {
        try
        {
            // Translate artist names to Chinese for better QQ Music search results
            ArtistHelper.ChineselizeArtists(state.Artists);

            var generalSearch = await SearchHelper.Search(new TrackMultiArtistMetadata()
            {
                Album = state.Album,
                AlbumArtists = state.Artists,
                Artists = state.Artists,
                DurationMs = (int)state.Duration.TotalMilliseconds,
                Title = state.Title,
            }, Searchers.QQMusic, CompareHelper.MatchType.Medium) as QQMusicSearchResult;

            return generalSearch;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private List<LyricsLine>? ParseLyrics(string lrc, LyricsRawTypes type)
    {
        var lyricsData = ParseHelper.ParseLyrics(lrc, type);
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
                    tokens.Add(new LyricsToken(TimeSpan.FromMilliseconds(syllable.StartTime), TimeSpan.FromMilliseconds(syllable.EndTime), syllable.Text));
                }
            }

            result.Add(new LyricsLine(TimeSpan.FromMilliseconds((long)line.StartTime), decoded, tokens.Count > 0 ? tokens : null));
        }

        return result;
    }
}