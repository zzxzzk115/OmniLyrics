using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Shared;

namespace OmniLyrics.Core.Favorites;

public enum YesPlayMusicAuthorizationState { SignInRequired, LocalSession, Authorized, Expired }
public enum YesPlayMusicQrStatus { AwaitingScan, AwaitingConfirmation }

/// <summary>YesPlayMusic's local player and bundled NetEase API, with a separate QR-authorized session.</summary>
public sealed class YesPlayMusicFavorites : ITrackFavorites, IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _favoriteGate = new(1, 1);
    private FavoriteState? _cached;
    private string? _cachedCookie;
    private DateTimeOffset _cachedAt, _retryAfter;
    public YesPlayMusicFavorites(HttpMessageHandler? handler = null, TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _http = new(handler ?? new HttpClientHandler
            { UseProxy = false, AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
    }
    private void Remember(FavoriteState state, string? cookie)
    {
        _cached = state; _cachedCookie = cookie;
        _cachedAt = _time.GetUtcNow(); _retryAfter = DateTimeOffset.MinValue;
    }
    private static bool Transient(HttpRequestException e) => e.StatusCode == null
        || (int)e.StatusCode is 405 or 408 or 429 or >= 500;


    private async Task<JsonElement> ApiAsync(string path, CancellationToken token, string? cookie, Dictionary<string, string?>? query = null)
    {
        query ??= new();
        query["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Get, QueryHelpers.AddQueryString("http://127.0.0.1:10754/" + path, query));
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("Cookie", string.Join("; ", cookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        return json.RootElement.Clone();
    }
    public async Task SignInAsync(Func<byte[], Task> showQr, CancellationToken token, Func<YesPlayMusicQrStatus, Task>? progress = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        try
        {
            var keyResponse = await ApiAsync("login/qr/key", ct, null).ConfigureAwait(false);
            var key = keyResponse.GetProperty("data").GetProperty("unikey").GetString();
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException(Localization.Get("YesPlayMusicLoginFailed"));
            var qr = await ApiAsync("login/qr/create", ct, null, new() { ["key"] = key, ["qrimg"] = "true" }).ConfigureAwait(false);
            var image = qr.GetProperty("data").GetProperty("qrimg").GetString();
            const string prefix = "data:image/png;base64,";
            if (image == null || !image.StartsWith(prefix, StringComparison.Ordinal) || image.Length > 500000)
                throw new InvalidOperationException(Localization.Get("YesPlayMusicLoginFailed"));
            await showQr(Convert.FromBase64String(image[prefix.Length..])).ConfigureAwait(false);
            var lastStatus = YesPlayMusicQrStatus.AwaitingScan;
            if (progress != null) await progress(lastStatus).ConfigureAwait(false);
            while (true)
            {
                await Task.Delay(2000, ct).ConfigureAwait(false);
                var status = await ApiAsync("login/qr/check", ct, null, new() { ["key"] = key }).ConfigureAwait(false);
                switch (status.GetProperty("code").GetInt32())
                {
                    case 800: throw new TimeoutException(Localization.Get("YesPlayMusicQrExpired"));
                    case 801: case 802:
                        var currentStatus = status.GetProperty("code").GetInt32() == 802
                            ? YesPlayMusicQrStatus.AwaitingConfirmation : YesPlayMusicQrStatus.AwaitingScan;
                        if (progress != null && currentStatus != lastStatus) await progress(currentStatus).ConfigureAwait(false);
                        lastStatus = currentStatus;
                        continue;
                    case 803:
                        var cookie = status.GetProperty("cookie").GetString();
                        if (string.IsNullOrWhiteSpace(cookie) || await UserIdAsync(cookie, ct).ConfigureAwait(false) == null)
                            throw new InvalidOperationException(Localization.Get("YesPlayMusicLoginFailed"));
                        ct.ThrowIfCancellationRequested();
                        UserConfiguration.SaveYesPlayMusicCookie(cookie);
                        return;
                    default: throw new InvalidOperationException(Localization.Get("YesPlayMusicLoginFailed"));
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw new TimeoutException(Localization.Get("YesPlayMusicQrExpired"));
        }
    }

    public async Task<bool> HasLocalSessionAsync(CancellationToken token = default) =>
        await UserIdAsync(null, token).ConfigureAwait(false) != null;

    public async Task<YesPlayMusicAuthorizationState> CheckAuthorizationAsync(CancellationToken token = default)
    {
        var cookie = UserConfiguration.ReadYesPlayMusicCookie();
        var signedIn = await UserIdAsync(cookie, token).ConfigureAwait(false) != null;
        if (!string.IsNullOrEmpty(cookie)) return signedIn ? YesPlayMusicAuthorizationState.Authorized : YesPlayMusicAuthorizationState.Expired;
        return signedIn ? YesPlayMusicAuthorizationState.LocalSession : YesPlayMusicAuthorizationState.SignInRequired;
    }

    private async Task<long?> UserIdAsync(string? cookie, CancellationToken token)
    {
        var response = await ApiAsync("user/account", token, cookie).ConfigureAwait(false);
        return response.TryGetProperty("code", out var code) && code.GetInt32() == 200
            && response.TryGetProperty("profile", out var profile) && profile.ValueKind == JsonValueKind.Object
            && profile.TryGetProperty("userId", out var id) && id.TryGetInt64(out var value) && value > 0 ? value : null;
    }
    private async Task<string?> CurrentIdAsync(PlayerState expected, CancellationToken token)
    {
        using var response = await _http.GetAsync("http://127.0.0.1:27232/player", token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        var track = json.RootElement.GetProperty("currentTrack");
        if (track.ValueKind != JsonValueKind.Object || !FavoriteIdentity.Matches(expected, track.GetProperty("name").GetString(),
            track.GetProperty("ar").EnumerateArray().Select(a => a.GetProperty("name").GetString() ?? ""),
            track.GetProperty("al").GetProperty("name").GetString(), track.GetProperty("dt").GetDouble())) return null;
        return track.GetProperty("id").TryGetInt64(out var id) && id > 0 ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }
    private async Task<FavoriteState?> ReadAsync(PlayerState expected, string? cookie, CancellationToken token)
    {
        var id = await CurrentIdAsync(expected, token).ConfigureAwait(false);
        if (id == null || await UserIdAsync(cookie, token).ConfigureAwait(false) is not { } uid) return null;
        var list = await ApiAsync("likelist", token, cookie, new() { ["uid"] = uid.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ConfigureAwait(false);
        if (list.GetProperty("code").GetInt32() != 200 || await CurrentIdAsync(expected, token).ConfigureAwait(false) != id
            || UserConfiguration.ReadYesPlayMusicCookie() != cookie) return null;
        return new(id, LyricsCache.TrackKey(expected), list.GetProperty("ids").EnumerateArray().Any(value => value.ToString() == id));
    }
    public async Task<FavoriteState?> GetFavoriteAsync(PlayerState expected, CancellationToken token = default)
    {
        try
        {
            await _favoriteGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var cookie = UserConfiguration.ReadYesPlayMusicCookie();
                if (cookie != _cachedCookie) { _cached = null; _retryAfter = DateTimeOffset.MinValue; _cachedCookie = cookie; }
                var id = await CurrentIdAsync(expected, token).ConfigureAwait(false);
                if (id == null || UserConfiguration.ReadYesPlayMusicCookie() != cookie) return null;
                var now = _time.GetUtcNow();
                var sameTrack = _cached?.TrackId == id && _cached.MediaKey == LyricsCache.TrackKey(expected);
                if (sameTrack && now - _cachedAt < TimeSpan.FromSeconds(30)) return _cached;
                var recent = sameTrack && now - _cachedAt < TimeSpan.FromMinutes(2) ? _cached : null;
                if (now < _retryAfter) return recent;
                try
                {
                    // GUI and shared-service clients use the same gate/cache. Always
                    // verify the local track, but avoid repeated cloud library reads.
                    var result = await ReadAsync(expected, cookie, token).ConfigureAwait(false);
                    if (result != null) Remember(result, cookie);
                    else _cached = null;
                    return result;
                }
                catch (HttpRequestException e) when (Transient(e))
                {
                    _retryAfter = _time.GetUtcNow().AddSeconds(30);
                    return recent;
                }
            }
            finally { _favoriteGate.Release(); }
        }
        catch (Exception e) when (Unavailable(e)) { return null; }
    }
    public async Task<FavoriteState?> SetFavoriteAsync(PlayerState expected, FavoriteState previous, bool favorite, CancellationToken token = default)
    {
        try
        {
            await _favoriteGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var cookie = UserConfiguration.ReadYesPlayMusicCookie();
                if (previous.MediaKey != LyricsCache.TrackKey(expected)
                    || await CurrentIdAsync(expected, token).ConfigureAwait(false) != previous.TrackId
                    || UserConfiguration.ReadYesPlayMusicCookie() != cookie
                    || await UserIdAsync(cookie, token).ConfigureAwait(false) == null) return null;
                _cached = null;
                var result = await ApiAsync("like", token, cookie, new() { ["id"] = previous.TrackId, ["like"] = favorite ? "true" : "false" }).ConfigureAwait(false);
                if (result.GetProperty("code").GetInt32() != 200) return null;
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    if (attempt > 0) await Task.Delay(400, token).ConfigureAwait(false);
                    var confirmed = await ReadAsync(expected, cookie, token).ConfigureAwait(false);
                    if (confirmed == null || confirmed.TrackId != previous.TrackId) return null;
                    if (confirmed.IsFavorite == favorite) { Remember(confirmed, cookie); return confirmed; }
                }
            }
            catch (HttpRequestException e) when (Transient(e)) { _retryAfter = _time.GetUtcNow().AddSeconds(30); }
            finally { _favoriteGate.Release(); }
        }
        catch (Exception e) when (Unavailable(e)) { }
        return null;
    }
    private static bool Unavailable(Exception e) => e is HttpRequestException or OperationCanceledException or JsonException
        or InvalidOperationException or KeyNotFoundException or FormatException;
    public void Dispose() => _http.Dispose();
}
