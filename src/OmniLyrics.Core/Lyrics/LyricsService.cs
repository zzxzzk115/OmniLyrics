using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Providers.Web.QQMusic;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Helpers;
using System.Net;

namespace OmniLyrics.Core.Lyrics;

public class LyricsService
{
    private readonly Api _api = new();

    private readonly YesPlayMusicApi _yesPlayMusicsLyricsApi = new();

    public async Task<List<LyricsLine>?> SearchLyricsAsync(PlayerState state)
    {
        try
        {
            // Try get lyrics from YesPlayMusic first
            var app = state.SourceApp ?? "";
            if (app.StartsWith("YesPlayMusic"))
            {
                var embedLyrics = await _yesPlayMusicsLyricsApi.TryGetLyricsAsync();
                if (embedLyrics != null)
                {
                    return ParseLrc(embedLyrics);
                }
            }

            // Translate artists to Chinese for better QQ Music search results
            ArtistHelper.ChineselizeArtists(state.Artists);

            var generalSearch = await SearchHelper.Search(new TrackMultiArtistMetadata()
            {
                Album = state.Album,
                AlbumArtists = state.Artists,
                Artists = state.Artists,
                DurationMs = (int)state.Duration.TotalMilliseconds,
                Title = state.Title,
            }, Searchers.QQMusic, CompareHelper.MatchType.Medium);

            if (generalSearch == null)
                return null;

            var search = await _api.Search(generalSearch.Title + " " + generalSearch.Artist, Api.SearchTypeEnum.SONG_ID);
            if (search == null)
                return null;

            var lyrics = await _api.GetLyric(search.Req_1.Data.Body.Song.List.First().Mid);
            if (lyrics == null)
                return null;

            return ParseLrc(lyrics.Lyric);
        }
        catch(Exception)
        {
            return null;
        }
    }

    private List<LyricsLine> ParseLrc(string lrc)
    {
        var lyricsData = ParseHelper.ParseLyrics(lrc, LyricsRawTypes.Lrc);
        if (lyricsData == null || lyricsData.Lines == null)
            return null;

        var result = new List<LyricsLine>();
        foreach (var line in lyricsData.Lines)
        {
            if (line.StartTime == null || string.IsNullOrWhiteSpace(line.Text))
                continue;

            // Decode HTML entities to normal characters
            string decoded = WebUtility.HtmlDecode(line.Text);

            result.Add(new LyricsLine(TimeSpan.FromMilliseconds((long)line.StartTime), decoded));
        }

        return result;
    }
}