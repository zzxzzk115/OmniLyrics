using System.Text.Json;

using OmniLyrics.Core.Lyrics;
using OmniLyrics.Core.Lyrics.Models;
using Lyricify.Lyrics.Models;

namespace OmniLyrics.Core.Helpers;

/// <summary>
///     YesPlayMusic unified state provider.
///     Requests /player only once per refresh and returns full PlayerState.
///     https://github.com/zzxzzk115/i3status-rust-ypm-lyrics
/// </summary>
public class YesPlayMusicApi : IDisposable
{
    private readonly HttpClient _lyric = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:10754")
    };

    private readonly HttpClient _player = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:27232")
    };

    private PlayerState? _cachedState;

    public void Dispose()
    {
        _player.Dispose();
        _lyric.Dispose();
    }

    /// <summary>
    ///     Returns a refreshed PlayerState built from /player API.
    ///     Returns null if YPM not running or no track playing.
    /// </summary>
    public async Task<PlayerState?> GetStateAsync()
    {
        try
        {
            using var resp = await _player.GetAsync("/player");
            if (!resp.IsSuccessStatusCode)
                return null;

            using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

            if (!json.RootElement.TryGetProperty("currentTrack", out var track))
                return null;

            // read progress
            double progress = json.RootElement.TryGetProperty("progress", out var p)
                ? p.GetDouble()
                : 0;

            // parse metadata
            string? title = track.TryGetProperty("name", out var t) ? t.GetString() : "";
            int durationMs = track.TryGetProperty("dt", out var dt) ? dt.GetInt32() : 0;

            string? album = null;
            string? artwork = null;

            if (track.TryGetProperty("al", out var albumNode))
            {
                album = albumNode.TryGetProperty("name", out var nameNode)
                    ? nameNode.GetString()
                    : null;

                artwork = albumNode.TryGetProperty("picUrl", out var pic)
                    ? pic.GetString()
                    : null;
            }

            var artists = new List<string>();
            if (track.TryGetProperty("ar", out var arNode) && arNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arNode.EnumerateArray())
                {
                    if (a.TryGetProperty("name", out var an))
                        artists.Add(an.GetString()!);
                }
            }

            // build PlayerState
            var state = new PlayerState
            {
                Title = title,
                Album = album,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                Position = TimeSpan.FromSeconds(progress),
                Playing = true,
                SourceApp = "YesPlayMusic",
                ArtworkUrl = artwork
            };

            state.Artists.AddRange(artists);

            _cachedState = state;
            return state;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Fetch raw LRC lyrics of the currently playing track.
    /// </summary>
    public async Task<string?> TryGetLyricsAsync(PlayerState? expected = null)
    {
        long? id = await TryGetCurrentTrackIdAsync(expected);
        if (id is null)
            return null;

        return await TryGetLyricsRawAsync(id.Value);
    }

    private async Task<long?> TryGetCurrentTrackIdAsync(PlayerState? expected)
    {
        try
        {
            using var resp = await _player.GetAsync("/player");
            if (!resp.IsSuccessStatusCode)
                return null;

            using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

            if (json.RootElement.TryGetProperty("currentTrack", out var track) &&
                track.TryGetProperty("id", out var id))
            {
                if (expected != null)
                {
                    static string Norm(string? text) => (text ?? "").Trim().ToLowerInvariant();
                    if (!track.TryGetProperty("name", out var name) || Norm(name.GetString()) != Norm(expected.Title)) return null;
                    if (!track.TryGetProperty("ar", out var artists) || artists.ValueKind != JsonValueKind.Array) return null;
                    var names = artists.EnumerateArray().Select(a => a.TryGetProperty("name", out var artist)
                        ? Norm(artist.GetString()) : "").ToHashSet();
                    if (!expected.Artists.All(a => names.Contains(Norm(a)))) return null;
                    if (!string.IsNullOrEmpty(expected.Album) && (!track.TryGetProperty("al", out var album)
                        || !album.TryGetProperty("name", out var albumName) || Norm(albumName.GetString()) != Norm(expected.Album))) return null;
                }
                return id.GetInt64();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<LyricsLine>?> TryGetLyricLinesAsync(PlayerState expected)
    {
        var id = await TryGetCurrentTrackIdAsync(expected);
        if (id == null) return null;
        try
        {
            using var response = await _lyric.GetAsync($"/lyric?id={id.Value}");
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string? Raw(string key) => json.RootElement.TryGetProperty(key, out var node)
                && node.TryGetProperty("lyric", out var text) ? text.GetString() : null;
            var raw = Raw("yrc");
            if (string.IsNullOrWhiteSpace(raw)) raw = Raw("lrc");
            return string.IsNullOrWhiteSpace(raw) ? null : LyricsService.AttachTranslation(
                LyricsService.ParseLyrics(raw, LyricsRawTypes.Unknown), Raw("ytlrc") ?? Raw("tlyric"));
        }
        catch { return null; }
    }

    private async Task<string?> TryGetLyricsRawAsync(long trackId)
    {
        try
        {
            using var resp = await _lyric.GetAsync($"/lyric?id={trackId}");
            if (!resp.IsSuccessStatusCode)
                return null;

            using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

            if (json.RootElement.TryGetProperty("lrc", out var lrcNode) &&
                lrcNode.TryGetProperty("lyric", out var lyric))
            {
                return lyric.GetString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
