using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Favorites;

internal static class ProviderChecks
{
    private static int _passed;
    private static void Check(string name, bool condition)
    { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); _passed++; }
    private static HttpResponseMessage Json(object value, HttpStatusCode code = HttpStatusCode.OK) => new(code)
        { Content = new StringContent(JsonSerializer.Serialize(value)) };
    public static async Task RunAsync()
    {
        await SpotifyAsync();
        await YesPlayMusicAsync();
        await YesPlayMusicPollingAsync();
        Console.WriteLine($"{_passed} additional favorites checks passed");
        Directory.Delete(UserConfiguration.DirectoryPath, true);
    }
    private static async Task SpotifyAsync()
    {
        var client = "test-client";
        UserConfiguration.SaveSpotifyClientId(client);
        var refreshes = 0;
        string? challenge = null, redirect = null;
        using var authorization = new SpotifyAuthorization(new Handler(async request =>
        {
            Check("Tokens only go to the Spotify token endpoint", request.RequestUri!.AbsoluteUri == "https://accounts.spotify.com/api/token");
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
            if (form["grant_type"] == "authorization_code")
            {
                Check("PKCE verifier matches browser challenge", challenge == WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString()))));
                Check("Token exchange uses the same dynamic loopback redirect", form["redirect_uri"] == redirect && form["code"] == "test-code" && !form.ContainsKey("client_secret"));
            }
            else refreshes++;
            return Json(new { access_token = "test-access", refresh_token = "test-refresh", scope = SpotifyAuthorization.Scopes, expires_in = 3600 });
        }));
        await authorization.SignInAsync(client, async uri =>
        {
            var query = QueryHelpers.ParseQuery(uri.Query);
            challenge = query["code_challenge"]; redirect = query["redirect_uri"];
            Check("Authorization uses Spotify and S256", uri.Host == "accounts.spotify.com" && query["code_challenge_method"] == "S256");
            var callback = new Uri(redirect!);
            Check("Callback only listens on an ephemeral IPv4 loopback port", callback.Host == "127.0.0.1" && callback.Port > 0 && callback.AbsolutePath == "/spotify/callback");
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
            using var wrong = await http.GetAsync(redirect + "?state=wrong&code=wrong");
            Check("Wrong OAuth state is rejected", wrong.StatusCode == HttpStatusCode.BadRequest);
            using var right = await http.GetAsync(QueryHelpers.AddQueryString(redirect!, new Dictionary<string, string?> { ["state"] = query["state"], ["code"] = "test-code" }));
            Check("Valid callback completes and returns a no-store page", right.IsSuccessStatusCode && right.Headers.CacheControl?.NoStore == true);
        }, default);
        var saved = UserConfiguration.ReadSpotifyTokens();
        Check("Authorization persists requested scopes privately", saved?.Scope == SpotifyAuthorization.Scopes
            && !UserConfiguration.ReadConfigurationText().Contains("test-access"));
        if (!OperatingSystem.IsWindows()) Check("Token file is owner-only", File.GetUnixFileMode(Path.Combine(UserConfiguration.DirectoryPath, "spotify-authorization.json")) == (UnixFileMode.UserRead | UnixFileMode.UserWrite));
        var state = new PlayerState { Title = "Song", Artists = ["Artist"], Album = "Album", Duration = TimeSpan.FromSeconds(120), SourceApp = "Spotify" };
        var id = "one"; var liked = false; var writes = 0; var unauthorized = false; var rejected = false; var rateLimited = false; var reads = 0;
        using var api = new SpotifyFavorites(new Handler(request =>
        {
            Check("Spotify library request contains a bearer token", request.Headers.Authorization?.Parameter == "test-access");
            reads++;
            if (unauthorized) { unauthorized = false; return new(HttpStatusCode.Unauthorized); }
            if (rateLimited)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(60)); return response;
            }
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("currently-playing")) return Json(new { item = new { type = "track", is_local = false, name = "Song", artists = new[] { new { name = "Artist" } }, album = new { name = "Album" }, duration_ms = 120000, uri = "spotify:track:" + id } });
            if (path.EndsWith("contains")) return Json(new[] { liked });
            Check("Spotify mutation targets the explicit track URI", QueryHelpers.ParseQuery(request.RequestUri.Query)["uris"] == "spotify:track:" + id);
            writes++;
            if (rejected) return new(HttpStatusCode.Forbidden);
            Check("Spotify uses the library PUT / DELETE endpoint", path == "/v1/me/library" && (request.Method == HttpMethod.Put || request.Method == HttpMethod.Delete));
            liked = request.Method == HttpMethod.Put;
            return new(HttpStatusCode.OK) { Content = new StringContent("") };
        }), authorization);
        var snapshot = await api.GetFavoriteAsync(state);
        Check("Spotify reads favorites without mutation", snapshot is { IsFavorite: false } && writes == 0);
        Check("Spotify saves and confirms", (await api.SetFavoriteAsync(state, snapshot!, true)) is { IsFavorite: true } && writes == 1);
        Check("Spotify removes and confirms", (await api.SetFavoriteAsync(state, snapshot!, false)) is { IsFavorite: false } && writes == 2);
        id = "two";
        Check("Spotify rejects stale track IDs", await api.SetFavoriteAsync(state, snapshot!, true) == null && writes == 2);
        id = "one"; rejected = true;
        Check("Spotify refuses rejected writes without false success", await api.SetFavoriteAsync(state, snapshot!, true) == null && !liked && writes == 3);
        rejected = false; unauthorized = true;
        Check("Spotify refreshes once after a rejected read", await api.GetFavoriteAsync(state) != null && refreshes == 1);
        var wrongSong = state.DeepCopy(); wrongSong.Title = "Different";
        Check("Spotify rejects mismatched desktop / account tracks", await api.GetFavoriteAsync(wrongSong) == null);
        rateLimited = true;
        Check("Spotify hides unavailable library capability", await api.GetFavoriteAsync(state) == null);
        var before = reads;
        Check("Spotify honors Retry-After across polls", await api.GetFavoriteAsync(state) == null && reads == before);
        UserConfiguration.SaveSpotifyTokens(saved! with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-10) });
        Check("Expired Spotify access is refreshed", await authorization.AccessTokenAsync(default) != null && refreshes == 2);
        UserConfiguration.SaveSpotifyTokens(saved! with { Scope = "user-read-currently-playing" });
        Check("Missing library scopes disable favorites", await authorization.AccessTokenAsync(default) == null);
        UserConfiguration.ClearSpotifyTokens();
        using var cancel = new CancellationTokenSource();
        try { await authorization.SignInAsync(client, _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check("Cancelled OAuth cannot save credentials", UserConfiguration.ReadSpotifyTokens() == null); }
        Check("OAuth state comparison rejects empty and oversized input", !SpotifyAuthorization.ValidState("state", null) && !SpotifyAuthorization.ValidState("state", new string('x', 300)));
        UserConfiguration.SaveSpotifyTokens(saved!);
        UserConfiguration.SaveSpotifyClientId("another-client");
        Check("Changing Client ID clears incompatible credentials", UserConfiguration.ReadSpotifyTokens() == null);
        File.WriteAllText(Path.Combine(UserConfiguration.DirectoryPath, "spotify-authorization.json"), "{}");
        Check("Malformed saved credentials are unavailable", UserConfiguration.ReadSpotifyTokens() == null);
    }
    private static async Task YesPlayMusicAsync()
    {
        var state = new PlayerState { Title = "Song", Artists = ["Artist"], Album = "Album", Duration = TimeSpan.FromSeconds(120), SourceApp = "YesPlayMusic" };
        var id = 12; var liked = false; var writes = 0; var fail = false; var qrShown = false; var localSession = false;
        var qrChecks = 0; var qrExpired = false; var rejectSession = false;
        using var api = new YesPlayMusicFavorites(new Handler(request =>
        {
            var uri = request.RequestUri!;
            Check("YesPlayMusic only uses loopback without credentials in URL", uri.Host == "127.0.0.1" && !uri.Query.Contains("MUSIC_U"));
            if (uri.Port == 27232)
            {
                Check("Player endpoint does not receive login cookies", !request.Headers.Contains("Cookie"));
                return Json(new { currentTrack = new { id, name = "Song", ar = new[] { new { name = "Artist" } }, al = new { name = "Album" }, dt = 120000 } });
            }
            if (uri.AbsolutePath == "/login/qr/key") return Json(new { data = new { unikey = "test-key" } });
            if (uri.AbsolutePath == "/login/qr/create") return Json(new { data = new { qrimg = "data:image/png;base64,AQID" } });
            if (uri.AbsolutePath == "/login/qr/check") return Json(new { code = qrExpired ? 800 : ++qrChecks < 3 ? 800 + qrChecks : 803, cookie = "MUSIC_U=test-cookie;" });
            if (!request.Headers.Contains("Cookie"))
            {
                if (!localSession) return Json(new { code = 301 });
            }
            else Check("Local NetEase API receives the authorized session", request.Headers.GetValues("Cookie").Single() == "MUSIC_U=test-cookie");
            if (uri.AbsolutePath == "/user/account") return Json(new { code = 200, profile = rejectSession ? null : new { userId = 15 } });
            if (uri.AbsolutePath == "/likelist") return Json(new { code = 200, ids = liked ? new[] { 12 } : Array.Empty<int>() });
            Check("YesPlayMusic mutation uses an explicit song ID", uri.AbsolutePath == "/like" && QueryHelpers.ParseQuery(uri.Query)["id"] == id.ToString());
            writes++;
            if (fail) return Json(new { code = 403 });
            liked = QueryHelpers.ParseQuery(uri.Query)["like"] == "true";
            return Json(new { code = 200 });
        }));
        Check("YesPlayMusic hides favorites when the local API requires sign-in", await api.GetFavoriteAsync(state) == null && writes == 0);
        Check("The local session check distinguishes unsigned access", !await api.HasLocalSessionAsync());
        Check("Settings report a sign-in requirement without a session", await api.CheckAuthorizationAsync() == YesPlayMusicAuthorizationState.SignInRequired);
        localSession = true;
        Check("The local session check detects existing API access", await api.HasLocalSessionAsync());
        Check("Settings accept a local session without QR authorization", await api.CheckAuthorizationAsync() == YesPlayMusicAuthorizationState.LocalSession);
        var local = await api.GetFavoriteAsync(state);
        Check("YesPlayMusic uses an existing local API session without saved credentials", local is { IsFavorite: false } && UserConfiguration.ReadYesPlayMusicCookie() == null);
        Check("YesPlayMusic can favorite directly through a signed-in local API", (await api.SetFavoriteAsync(state, local!, true)) is { IsFavorite: true });
        liked = false; writes = 0; localSession = false;
        var progress = new List<YesPlayMusicQrStatus>();
        await api.SignInAsync(bytes => { qrShown = bytes.SequenceEqual(new byte[] { 1, 2, 3 }); return Task.CompletedTask; }, default,
            status => { progress.Add(status); return Task.CompletedTask; });
        Check("QR login distinguishes waiting for scan and phone confirmation", progress.SequenceEqual(new[] { YesPlayMusicQrStatus.AwaitingScan, YesPlayMusicQrStatus.AwaitingConfirmation }));
        Check("QR login stores a verified session", qrShown && UserConfiguration.ReadYesPlayMusicCookie() == "MUSIC_U=test-cookie;");
        Check("Settings check the saved QR session, not anonymous API access", await api.CheckAuthorizationAsync() == YesPlayMusicAuthorizationState.Authorized && !await api.HasLocalSessionAsync());
        rejectSession = true;
        Check("Settings distinguish expired credentials from unavailable APIs", await api.CheckAuthorizationAsync() == YesPlayMusicAuthorizationState.Expired);
        rejectSession = false;
        var snapshot = await api.GetFavoriteAsync(state);
        Check("YesPlayMusic reads current favorite state", snapshot is { IsFavorite: false } && writes == 0);
        Check("YesPlayMusic likes and confirms", (await api.SetFavoriteAsync(state, snapshot!, true)) is { IsFavorite: true } && writes == 1);
        Check("YesPlayMusic unlikes and confirms", (await api.SetFavoriteAsync(state, snapshot!, false)) is { IsFavorite: false } && writes == 2);
        id = 13;
        Check("YesPlayMusic rejects stale song IDs", await api.SetFavoriteAsync(state, snapshot!, true) == null && writes == 2);
        id = 12; fail = true;
        Check("YesPlayMusic does not report rejected writes as success", await api.SetFavoriteAsync(state, snapshot!, true) == null && !liked && writes == 3);
        UserConfiguration.ClearYesPlayMusicCookie();
        Check("Disconnect removes YesPlayMusic capability", await api.GetFavoriteAsync(state) == null);
        qrExpired = true;
        try { await api.SignInAsync(_ => Task.CompletedTask, default); throw new Exception("Expired QR accepted"); }
        catch (TimeoutException) { Check("Expired QR reports expiry without saving a session", UserConfiguration.ReadYesPlayMusicCookie() == null); }
        qrExpired = false;
        using var cancel = new CancellationTokenSource();
        try { await api.SignInAsync(_ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check("Cancelled QR login cannot save credentials", UserConfiguration.ReadYesPlayMusicCookie() == null); }
        try { UserConfiguration.SaveYesPlayMusicCookie("x\r\nInjected: header"); throw new Exception("Invalid cookie accepted"); }
        catch (InvalidDataException) { Check("Cookie header injection is rejected", true); }
    }
    private static async Task YesPlayMusicPollingAsync()
    {
        var state = new PlayerState { Title = "Song", Artists = ["Artist"], Album = "Album", Duration = TimeSpan.FromSeconds(120), SourceApp = "YesPlayMusic" };
        var clock = new PollingTime();
        var reads = 0; var id = 12; var blocked = false; var liked = true;
        using var api = new YesPlayMusicFavorites(new Handler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/player") return Json(new { currentTrack = new { id, name = "Song", ar = new[] { new { name = "Artist" } }, al = new { name = "Album" }, dt = 120000 } });
            if (path == "/user/account") return Json(new { code = 200, profile = new { userId = 15 } });
            if (path == "/likelist")
            {
                reads++;
                await Task.Delay(40);
                return blocked ? Json(new { code = 405 }, HttpStatusCode.MethodNotAllowed)
                    : Json(new { code = 200, ids = liked ? new[] { 12 } : Array.Empty<int>() });
            }
            if (path == "/like") { liked = QueryHelpers.ParseQuery(request.RequestUri.Query)["like"] == "true"; return Json(new { code = 200 }); }
            throw new Exception("Unexpected request");
        }), clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.GetFavoriteAsync(state)));
        Check("Concurrent GUI and widget polls share one cloud favorite read", reads == 1 && results.All(r => r is { IsFavorite: true }));
        clock.Advance(31); blocked = true;
        Check("Transient library failure retains the recent confirmed state", (await api.GetFavoriteAsync(state)) is { IsFavorite: true } && reads == 2);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.GetFavoriteAsync(state)));
        Check("Rate-limit backoff avoids repeated library requests", reads == 2);
        id = 13;
        Check("A cached favorite never follows a different local song ID", await api.GetFavoriteAsync(state) == null && reads == 2);
        id = 12; clock.Advance(91);
        Check("An unavailable library cannot keep a stale favorite forever", await api.GetFavoriteAsync(state) == null);
        clock.Advance(31); blocked = false; liked = false;
        var current = await api.GetFavoriteAsync(state);
        Check("Library reads recover after backoff and refresh external changes", current is { IsFavorite: false });
        var beforeWrite = reads;
        Check("Explicit writes bypass the cache and confirm the new state", (await api.SetFavoriteAsync(state, current!, true)) is { IsFavorite: true } && reads == beforeWrite + 1);
        Check("Subsequent polls reuse the confirmed write without another library request", (await api.GetFavoriteAsync(state)) is { IsFavorite: true } && reads == beforeWrite + 1);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Check("Cancelled polling returns safely without consuming the cache lock", await api.GetFavoriteAsync(state, cancel.Token) == null && await api.GetFavoriteAsync(state) != null);
        if (!Path.GetFullPath(UserConfiguration.DirectoryPath).StartsWith(Path.Combine(Path.GetTempPath(), "omnilyrics-favorites-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Credential test requires the isolated temporary configuration.");
        liked = false; UserConfiguration.SaveYesPlayMusicCookie("MUSIC_U=polling-test;");
        Check("Changing authorization invalidates the previous account's favorite cache", (await api.GetFavoriteAsync(state)) is { IsFavorite: false });
        UserConfiguration.ClearYesPlayMusicCookie();
    }
    private sealed class PollingTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;
        public Handler(Func<HttpRequestMessage, HttpResponseMessage> send) => _send = request => Task.FromResult(send(request));
        public Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return _send(request); }
    }
}
