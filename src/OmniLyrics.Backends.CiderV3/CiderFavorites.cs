using System.Net.Http.Json;
using System.Text.Json;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Shared;

namespace OmniLyrics.Backends.CiderV3;

/// <summary>Probe the library capability independently from playback/MPRIS support.</summary>
public sealed class CiderFavorites(string baseUrl = "http://127.0.0.1:10767") : ITrackFavorites, IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(3) };

    private async Task<JsonElement?> RequestAsync(string path, CancellationToken token, int? rating = null)
    {
        using var request = new HttpRequestMessage(rating.HasValue ? HttpMethod.Put : HttpMethod.Get, "/api/v2/" + path);
        var secret = UserConfiguration.ResolveCiderToken();
        if (!string.IsNullOrEmpty(secret)) request.Headers.Add("apptoken", secret);
        if (rating.HasValue) request.Content = JsonContent.Create(new { rating = rating.Value });
        using var response = await _http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) return null;
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object ? data.Clone() : null;
    }

    private async Task<string?> CurrentIdAsync(PlayerState expected, CancellationToken token)
    {
        var track = await RequestAsync("playback/now-playing", token);
        if (track == null) return null;
        string? Text(JsonElement obj, string name) => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        static string Norm(string? text) => new((text ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (Norm(Text(track.Value, "name")) != Norm(expected.Title)
            || Norm(Text(track.Value, "artistName")) != Norm(string.Join(", ", expected.Artists))) return null;
        if (!track.Value.TryGetProperty("playParams", out var parameters)) return null;
        var id = Text(parameters, "id");
        return string.IsNullOrEmpty(id) ? null : (Text(parameters, "kind") ?? "song") + ":" + id;
    }

    public async Task<FavoriteState?> GetFavoriteAsync(PlayerState expected, CancellationToken token = default)
    {
        try
        {
            var before = await CurrentIdAsync(expected, token);
            if (before == null) return null;
            var status = await RequestAsync("library/now-playing/status", token);
            if (status == null || !status.Value.TryGetProperty("rating", out var value)
                || !value.TryGetInt32(out var rating) || rating is < -1 or > 1) return null;
            if (await CurrentIdAsync(expected, token) != before) return null;
            return new(before, LyricsCache.TrackKey(expected), rating == 1);
        }
        catch { return null; }
    }

    public async Task<FavoriteState?> SetFavoriteAsync(PlayerState expected, FavoriteState previous, bool favorite, CancellationToken token = default)
    {
        try
        {
            if (previous.MediaKey != LyricsCache.TrackKey(expected) || await CurrentIdAsync(expected, token) != previous.TrackId) return null;
            // This Cider endpoint targets now-playing. Recheck immediately before
            // writing, and never retry the mutation against a different track.
            if (await RequestAsync("library/now-playing/rating", token, favorite ? 1 : 0) == null) return null;
            for (var i = 0; i < 7; i++)
            {
                if (i > 0) await Task.Delay(500, token);
                var state = await GetFavoriteAsync(expected, token);
                if (state == null || state.TrackId != previous.TrackId) return null;
                if (state.IsFavorite == favorite) return state;
            }
        }
        catch { }
        return null;
    }

    public void Dispose() => _http.Dispose();
}
