using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Core.Favorites;

public sealed class SpotifyAuthorization : IDisposable
{
    public const string RegisteredRedirectUri = "http://127.0.0.1/spotify/callback";
    public const string Scopes = "user-library-read user-library-modify user-read-currently-playing";
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);
    private readonly HttpClient _http;
    public SpotifyAuthorization(HttpMessageHandler? handler = null)
    {
        _http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    }
    public async Task SignInAsync(string clientId, Func<Uri, Task> openBrowser, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException(Localization.Get("SpotifyClientRequired"));
        UserConfiguration.SaveSpotifyClientId(clientId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var code = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        await using var server = builder.Build();
        server.MapGet("/spotify/callback", (HttpContext context) =>
        {
            if (context.Request.Host.Host != "127.0.0.1" || !ValidState(state, context.Request.Query["state"]))
                return Results.BadRequest("Invalid authorization state.");
            if (!string.IsNullOrEmpty(context.Request.Query["error"]))
                context.Response.OnCompleted(() => { code.TrySetException(new InvalidOperationException(Localization.Get("AuthorizationDenied"))); return Task.CompletedTask; });
            else if (context.Request.Query["code"].ToString() is { Length: > 0 and < 4096 } value)
                context.Response.OnCompleted(() => { code.TrySetResult(value); return Task.CompletedTask; });
            else return Results.BadRequest("Missing authorization code.");
            context.Response.Headers.CacheControl = "no-store";
            return Results.Text(Localization.Get("AuthorizationReturn"));
        });
        await server.StartAsync(ct).ConfigureAwait(false);
        try
        {
            var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var redirect = address + "/spotify/callback";
            await openBrowser(BuildAuthorizationUri(clientId.Trim(), redirect, state, verifier)).ConfigureAwait(false);
            var received = await code.Task.WaitAsync(ct).ConfigureAwait(false);
            var tokens = await ExchangeAsync(new() { ["client_id"] = clientId.Trim(), ["grant_type"] = "authorization_code",
                ["code"] = received, ["redirect_uri"] = redirect, ["code_verifier"] = verifier }, clientId.Trim(), null, ct).ConfigureAwait(false);
            if (UserConfiguration.LoadSpotifyClientId() != clientId.Trim()) throw new InvalidOperationException(Localization.Get("AuthorizationChanged"));
            ct.ThrowIfCancellationRequested();
            UserConfiguration.SaveSpotifyTokens(tokens);
        }
        finally { await server.StopAsync(CancellationToken.None).ConfigureAwait(false); }
    }
    internal static bool ValidState(string expected, string? actual) => actual is { Length: > 0 and < 256 }
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
    internal static Uri BuildAuthorizationUri(string clientId, string redirect, string state, string verifier) => new(QueryHelpers.AddQueryString(
        "https://accounts.spotify.com/authorize", new Dictionary<string, string?> {
            ["client_id"] = clientId, ["response_type"] = "code", ["redirect_uri"] = redirect, ["scope"] = Scopes,
            ["state"] = state, ["code_challenge_method"] = "S256",
            ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) }));
    public async Task<string?> AccessTokenAsync(CancellationToken token, bool forceRefresh = false)
    {
        await RefreshGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var saved = UserConfiguration.ReadSpotifyTokens();
            if (saved == null || saved.ClientId != UserConfiguration.LoadSpotifyClientId()) return null;
            if (!Scopes.Split(' ').All(scope => saved.Scope.Split(' ').Contains(scope))) return null;
            if (!forceRefresh && saved.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(60)) return saved.AccessToken;
            var refreshed = await ExchangeAsync(new() { ["client_id"] = saved.ClientId, ["grant_type"] = "refresh_token",
                ["refresh_token"] = saved.RefreshToken }, saved.ClientId, saved, token).ConfigureAwait(false);
            // Do not recreate credentials after a concurrent disconnect or account change.
            if (UserConfiguration.ReadSpotifyTokens() != saved || UserConfiguration.LoadSpotifyClientId() != saved.ClientId) return null;
            UserConfiguration.SaveSpotifyTokens(refreshed);
            return refreshed.AccessToken;
        }
        finally { RefreshGate.Release(); }
    }
    private async Task<SpotifyTokens> ExchangeAsync(Dictionary<string, string> values, string clientId, SpotifyTokens? previous, CancellationToken token)
    {
        using var response = await _http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(values), token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Localization.Get("SpotifyAuthorizationFailed"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        var root = json.RootElement;
        var access = root.GetProperty("access_token").GetString();
        var refresh = root.TryGetProperty("refresh_token", out var item) ? item.GetString() : previous?.RefreshToken;
        var scope = root.TryGetProperty("scope", out var scopes) ? scopes.GetString() : previous?.Scope;
        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh) || string.IsNullOrWhiteSpace(scope)
            || !Scopes.Split(' ').All(s => scope.Split(' ').Contains(s)))
            throw new InvalidOperationException(Localization.Get("SpotifyAuthorizationFailed"));
        var seconds = root.GetProperty("expires_in").GetInt32();
        if (seconds <= 0 || seconds > 86400) throw new InvalidOperationException(Localization.Get("SpotifyAuthorizationFailed"));
        return new(clientId, access, refresh, DateTimeOffset.UtcNow.AddSeconds(seconds), scope);
    }
    public void Dispose() => _http.Dispose();
}
