using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using OmniLyrics.Core.Shared;

namespace OmniLyrics.Core.Favorites;

public sealed class SpotifyFavorites : ITrackFavorites, IDisposable
{
    private readonly HttpClient _http;
    private readonly SpotifyAuthorization _authorization;
    private readonly bool _ownsAuthorization;
    private DateTimeOffset _retryAfter;
    public SpotifyFavorites(HttpMessageHandler? handler = null, SpotifyAuthorization? authorization = null)
    {
        _http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri("https://api.spotify.com/v1/"), Timeout = TimeSpan.FromSeconds(10) };
        _authorization = authorization ?? new();
        _ownsAuthorization = authorization == null;
    }
    private async Task<JsonElement?> RequestAsync(string path, HttpMethod method, CancellationToken token)
    {
        if (DateTimeOffset.UtcNow < _retryAfter) return null;
        var access = await _authorization.AccessTokenAsync(token).ConfigureAwait(false);
        for (var attempt = 0; access != null && attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _retryAfter = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30));
                return null;
            }
            // Only retry reads. A mutation is never automatically repeated.
            if (response.StatusCode == HttpStatusCode.Unauthorized && method == HttpMethod.Get && attempt == 0)
            { access = await _authorization.AccessTokenAsync(token, forceRefresh: true).ConfigureAwait(false); continue; }
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body);
            return json.RootElement.Clone();
        }
        return null;
    }
    private async Task<string?> CurrentIdAsync(PlayerState expected, CancellationToken token)
    {
        var root = await RequestAsync("me/player/currently-playing", HttpMethod.Get, token).ConfigureAwait(false);
        if (root is not { ValueKind: JsonValueKind.Object } value || !value.TryGetProperty("item", out var item)
            || item.ValueKind != JsonValueKind.Object || item.GetProperty("type").GetString() != "track"
            || item.TryGetProperty("is_local", out var local) && local.ValueKind == JsonValueKind.True) return null;
        if (!FavoriteIdentity.Matches(expected, item.GetProperty("name").GetString(),
            item.GetProperty("artists").EnumerateArray().Select(a => a.GetProperty("name").GetString() ?? ""),
            item.GetProperty("album").GetProperty("name").GetString(), item.GetProperty("duration_ms").GetDouble())) return null;
        var uri = item.GetProperty("uri").GetString();
        return uri is { Length: > 14 } && uri.StartsWith("spotify:track:", StringComparison.Ordinal)
            && uri[14..].All(char.IsAsciiLetterOrDigit) ? uri : null;
    }
    public async Task<FavoriteState?> GetFavoriteAsync(PlayerState expected, CancellationToken token = default)
    {
        try
        {
            var id = await CurrentIdAsync(expected, token).ConfigureAwait(false);
            if (id == null) return null;
            var state = await RequestAsync("me/library/contains?uris=" + Uri.EscapeDataString(id), HttpMethod.Get, token).ConfigureAwait(false);
            if (state is not { ValueKind: JsonValueKind.Array } items || items.GetArrayLength() != 1
                || items[0].ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || await CurrentIdAsync(expected, token).ConfigureAwait(false) != id) return null;
            return new(id, LyricsCache.TrackKey(expected), items[0].GetBoolean());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }
    public async Task<FavoriteState?> SetFavoriteAsync(PlayerState expected, FavoriteState previous, bool favorite, CancellationToken token = default)
    {
        try
        {
            if (previous.MediaKey != LyricsCache.TrackKey(expected) || await CurrentIdAsync(expected, token).ConfigureAwait(false) != previous.TrackId) return null;
            if (await RequestAsync("me/library?uris=" + Uri.EscapeDataString(previous.TrackId), favorite ? HttpMethod.Put : HttpMethod.Delete, token).ConfigureAwait(false) == null) return null;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                if (attempt > 0) await Task.Delay(400, token).ConfigureAwait(false);
                var confirmed = await GetFavoriteAsync(expected, token).ConfigureAwait(false);
                if (confirmed == null || confirmed.TrackId != previous.TrackId) return null;
                if (confirmed.IsFavorite == favorite) return confirmed;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException) { }
        return null;
    }
    public void Dispose() { _http.Dispose(); if (_ownsAuthorization) _authorization.Dispose(); }
}
