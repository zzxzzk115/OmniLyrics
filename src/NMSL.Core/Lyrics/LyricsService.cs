using NMSL.Core;
using NMSL.Core.Lyrics.Models;

using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Providers.Web.Netease;
using Lyricify.Lyrics.Searchers;

public class LyricsService
{
    private readonly Api _api = new();

    public async Task<List<LyricsLine>?> SearchLyricsAsync(PlayerState state)
    {
        try
        {
            var generalSearch = await SearchHelper.Search(new TrackMultiArtistMetadata()
            {
                Album = state.Album,
                AlbumArtists = state.Artists,
                Artists = state.Artists,
                DurationMs = (int)state.Duration.TotalMilliseconds,
                Title = state.Title,
            }, Searchers.Netease, Lyricify.Lyrics.Searchers.Helpers.CompareHelper.MatchType.Medium);

            if (generalSearch == null)
                return null;

            var search = await _api.SearchNew(generalSearch.Title + " " + generalSearch.Artist);
            if (search == null)
                return null;

            var lyrics = await _api.GetLyric(search.Result.Songs.First().Id);
            if (lyrics == null)
                return null;

            var lyricsData = ParseHelper.ParseLyrics(lyrics.Lrc.Lyric, LyricsRawTypes.Lrc);
            if (lyricsData == null || lyricsData.Lines == null)
                return null;

            var result = new List<LyricsLine>();
            foreach (var line in lyricsData.Lines)
            {
                if (line.StartTime == null || string.IsNullOrWhiteSpace(line.Text))
                    continue;
                result.Add(new LyricsLine(TimeSpan.FromMilliseconds((long)line.StartTime), line.Text));
            }

            return result;
        }
        catch(Exception)
        {
            return null;
        }
    }
}