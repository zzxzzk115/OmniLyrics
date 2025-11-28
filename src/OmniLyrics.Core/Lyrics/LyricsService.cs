using System.Net;

using OmniLyrics.Core;
using OmniLyrics.Core.Lyrics.Models;

using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Providers.Web.QQMusic;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;

public class LyricsService
{
    private readonly Api _api = new();

    public async Task<List<LyricsLine>?> SearchLyricsAsync(PlayerState state)
    {
        try
        {
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

            var lyricsData = ParseHelper.ParseLyrics(lyrics.Lyric, LyricsRawTypes.Lrc);
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
        catch(Exception)
        {
            return null;
        }
    }
}