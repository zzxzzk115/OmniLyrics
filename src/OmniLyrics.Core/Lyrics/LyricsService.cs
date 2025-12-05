using System.Net;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;
using OmniLyrics.Core.Helpers;
using OmniLyrics.Core.Lyrics.Models;

namespace OmniLyrics.Core.Lyrics;

public class LyricsService
{
    private readonly Lyricify.Lyrics.Providers.Web.Netease.Api _neteaseApi = new();
    private readonly Lyricify.Lyrics.Providers.Web.QQMusic.Api _qqMusicApi = new();

    private readonly YesPlayMusicApi _yesPlayMusicsLyricsApi = new();

    public async Task<List<LyricsLine>?> SearchLyricLinesAsync(PlayerState state, bool karaoke)
    {
        try
        {
            // Try get lyrics from YesPlayMusic first
            string app = state.SourceApp ?? "";
            if (app.Contains("yesplaymusic", StringComparison.OrdinalIgnoreCase))
            {
                // Use embeded lyrics (LRC)
                if (!karaoke)
                {
                    string? embededLyrics = await _yesPlayMusicsLyricsApi.TryGetLyricsAsync();
                    if (embededLyrics != null)
                    {
                        // YesPlayMusic only provides LRC format
                        return ParseLyrics(embededLyrics, LyricsRawTypes.Lrc);
                    }
                }
                // Use Netease API to get karaoke lyrics
                else
                {
                    var neteaseSong = await SearchNeteaseSongAsync(state);
                    if (neteaseSong == null)
                        return null;

                    var lyrics = await _neteaseApi.GetLyricNew(neteaseSong.Id);
                    if (lyrics == null)
                        return null;
                    
                    if (lyrics.Yrc != null)
                        return ParseLyrics(lyrics.Yrc.Lyric, LyricsRawTypes.Yrc);
                    
                    return ParseLyrics(lyrics.Lrc.Lyric,  LyricsRawTypes.Lrc);
                }
            }

            var qqSong = await SearchQQSongAsync(state);
            if (qqSong == null)
            {
                // Not found in QQ Music, fall back to Netease
                var neteaseSong = await SearchNeteaseSongAsync(state);
                if (neteaseSong == null)
                    return null;

                var lyrics = await _neteaseApi.GetLyricNew(neteaseSong.Id);
                if (lyrics == null)
                    return null;

                if (lyrics.Yrc != null)
                    return ParseLyrics(lyrics.Yrc.Lyric, LyricsRawTypes.Yrc);
                    
                return ParseLyrics(lyrics.Lrc.Lyric,  LyricsRawTypes.Lrc);
            }
            else
            {
                if (karaoke)
                {
                    var lyrics = await _qqMusicApi.GetLyricsAsync(qqSong.Id);
                    if (lyrics == null)
                        return null;

                    return ParseLyrics(lyrics.Lyrics!, LyricsRawTypes.Qrc);
                }
                else
                {
                    var lyrics = await _qqMusicApi.GetLyric(qqSong.Mid);
                    if (lyrics == null)
                        return null;

                    return ParseLyrics(lyrics.Lyric, LyricsRawTypes.Lrc);
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<QQMusicSearchResult?> SearchQQSongAsync(PlayerState state)
    {
        try
        {
            // Translate artist names to Chinese for better search results
            ArtistHelper.ChineselizeArtists(state.Artists);

            var generalSearch = await SearchHelper.Search(new TrackMultiArtistMetadata
            {
                Album = state.Album,
                AlbumArtists = state.Artists,
                Artists = state.Artists,
                DurationMs = (int)state.Duration.TotalMilliseconds,
                Title = state.Title
            }, Searchers.QQMusic, CompareHelper.MatchType.Medium) as QQMusicSearchResult;

            return generalSearch;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<Lyricify.Lyrics.Providers.Web.Netease.Song?> SearchNeteaseSongAsync(PlayerState state)
    {
        try
        {
            // Translate artist names to Chinese for better search results
            ArtistHelper.ChineselizeArtists(state.Artists);

            var neteaseSearch = await _neteaseApi.SearchNew(state.Title + " " + state.Artists.First());
            if (neteaseSearch == null)
                return null;

            return neteaseSearch.Result.Songs.First();
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