using System.Text.Json.Serialization;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using OmniLyrics.Core.Network;
using OmniLyrics.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OmniLyrics.Core;
using OmniLyrics.Core.Shared;

namespace OmniLyrics.Web;

public class WebApiServer : IAsyncDisposable
{
    private readonly IPlayerBackend _backend;
    private readonly ILyricsProvider _lyrics;
    private readonly int _port;
    private readonly string _listenAddress;
    private readonly LyricsManager _karaoke;
    private readonly object _snapshotGate = new();
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private WebApplication? _app;
    private readonly string? _ownerRole;
    private readonly LanSettings? _lan;
    private readonly IPAddress? _lanBindAddress;
    private readonly LanTrustStore? _trust;
    private readonly bool _localIpc;
    private readonly Func<LanSharingStatus>? _sharingStatus;
    private X509Certificate2? _certificate;
    private LanDiscovery? _discovery;
    private CancellationTokenSource? _discoveryCancellation;
    private Task? _discoveryTask;
    public bool IsRunning { get; private set; }

    public WebApiServer(IPlayerBackend backend, ILyricsProvider lyrics, int port = ClientServerCommonDefine.WebApiPort,
        string listenAddress = "127.0.0.1", LyricsManager? karaoke = null, string? ownerRole = null, LanSettings? lan = null, LanTrustStore? trust = null, IPAddress? lanBindAddress = null, bool localIpc = true, Func<LanSharingStatus>? sharingStatus = null)
    {
        _backend = backend;
        _lyrics = lyrics;
        _port = port;
        // Plain HTTP is a local IPC endpoint only. Old wildcard settings cannot expose it to the LAN.
        var parsed = IPAddress.Parse(listenAddress);
        _listenAddress = IPAddress.IsLoopback(parsed) ? parsed.ToString() : "127.0.0.1";
        _lan = lan; _lanBindAddress = lanBindAddress; _localIpc = localIpc; _sharingStatus = sharingStatus;
        _trust = lan?.Enabled == true ? trust ?? new LanTrustStore() : null;
        _karaoke = karaoke ?? new LyricsManager();
        _ownerRole = ownerRole;
    }

    public async Task StartAsync(CancellationToken token)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        // Do not allow environment/appsettings Kestrel endpoints to bypass our listener policy.
        builder.Configuration.Sources.Clear();

        // A hosting widget keeps stdout exclusively for its JSON protocol.
        builder.Logging.ClearProviders();
        // The owning frontend controls lifetime; Kestrel must not install its
        // own process signal handlers inside a GUI or widget process.
        builder.Services.AddSingleton<IHostLifetime, EmbeddedLifetime>();

        if (_trust != null) _certificate = _trust.Certificate();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 8192;
            options.Limits.MaxConcurrentConnections = 32;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            if (_localIpc) options.Listen(IPAddress.Parse(_listenAddress), _port);
            if (_lan?.Enabled == true)
            {
                if (_lanBindAddress == null) options.ListenAnyIP(_lan.HttpsPort, endpoint => endpoint.UseHttps(options => { options.ServerCertificate = _certificate!; options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13; }));
                else options.Listen(_lanBindAddress, _lan.HttpsPort, endpoint => endpoint.UseHttps(options => { options.ServerCertificate = _certificate!; options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13; }));
            }
        });

        var app = builder.Build();
        _app = app;
        var pairBudget = new RequestBudget(12, TimeSpan.FromMinutes(1));
        var apiBudget = new RequestBudget(120, TimeSpan.FromSeconds(1));
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            context.Response.Headers.CacheControl = "no-store";
            // Browser-origin calls are not part of this native API; block CSRF and DNS rebinding.
            if (request.Headers.ContainsKey("Origin") || request.Headers.ContainsKey("Sec-Fetch-Site"))
            { context.Response.StatusCode = 403; return; }
            if (!context.Request.IsHttps)
            {
                if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)
                    || !(IPAddress.TryParse(request.Host.Host, out var hostIp) && IPAddress.IsLoopback(hostIp)
                        || request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
                { context.Response.StatusCode = 403; return; }
                if (request.Path.StartsWithSegments("/lan")) { context.Response.StatusCode = 404; return; }
                await next(context); return; // Same-machine CLI/TUI/GUI remain authentication-free.
            }
            if (_trust == null || context.Connection.RemoteIpAddress is not { } remoteIp || !LanAddress.IsPrivate(remoteIp))
            { context.Response.StatusCode = 403; return; }
            if (!apiBudget.Take()) { context.Response.StatusCode = 429; return; }
            if (request.Path == "/lan/pair" && request.Method == "POST")
            {
                if (!pairBudget.Take()) { context.Response.StatusCode = 429; return; }
                await next(context); return;
            }
            var header = request.Headers.Authorization.ToString();
            var grant = header.StartsWith("Bearer ", StringComparison.Ordinal) ? _trust.Authorize(header[7..]) : null;
            if (grant == null) { context.Response.StatusCode = 401; return; }
            if (!grant.AllowControl && (request.Method != "GET" || request.Path.StartsWithSegments("/favorites")))
            { context.Response.StatusCode = 403; return; }
            await next(context);
        });
        app.MapPost("/lan/pair", (PairingRequest request) =>
        {
            var result = _trust?.Redeem(request, LanTrustStore.Name(_lan!.DeviceName));
            return result == null ? Results.Unauthorized() : Results.Json(result);
        });

        if (_sharingStatus != null && _lan == null)
            app.MapGet("/sharing", () => Results.Json(_sharingStatus()));

        // One response pairs lyrics and state from the same track. Timed lyrics
        // are loaded on demand, without delaying playback/status responses.
        app.MapGet("/snapshot", () =>
        {
            lock (_snapshotGate)
            {
                var state = _backend.GetCurrentState()?.DeepCopy();
                _ = _karaoke.UpdateAsync(state, karaoke: true);
                var lyrics = _karaoke.Capture(state);
                return Results.Json(new LyricsSnapshot(LyricsSnapshot.ServiceName,
                    LyricsSnapshot.CurrentProtocol, state, lyrics.Lines, lyrics.Loading,
                    $"{_instanceId}:{lyrics.Revision}", _ownerRole));
            }
        });

        // --------  Playback control routes  --------
        app.MapPost("/playback/play", () => _backend.PlayAsync());
        app.MapPost("/playback/pause", () => _backend.PauseAsync());
        app.MapPost("/playback/toggle", () => _backend.TogglePlayPauseAsync());
        app.MapPost("/playback/next", () => _backend.NextAsync());
        app.MapPost("/playback/prev", () => _backend.PreviousAsync());

        app.MapGet("/favorites", async (CancellationToken cancellation) =>
        {
            var state = _backend.GetCurrentState()?.DeepCopy();
            var result = state != null && _backend is ITrackFavorites favorites
                ? await favorites.GetFavoriteAsync(state, cancellation) : null;
            return result != null ? Results.Json(result) : Results.NotFound();
        });
        app.MapPost("/favorites", async (FavoriteRequest request, CancellationToken cancellation) =>
        {
            var state = _backend.GetCurrentState()?.DeepCopy();
            if (state == null || request.Previous == null || request.Previous.MediaKey != LyricsCache.TrackKey(state)) return Results.Conflict();
            if (_backend is not ITrackFavorites favorites) return Results.NotFound();
            var result = await favorites.SetFavoriteAsync(state, request.Previous, request.Favorite, cancellation);
            return result != null ? Results.Json(result) : Results.Conflict();
        });

        app.MapPost("/playback/seek", async (SeekRequest req) =>
        {
            if (!double.IsFinite(req.Position) || req.Position < 0 || req.Position > TimeSpan.MaxValue.TotalSeconds / 2)
                return Results.BadRequest();
            await _backend.SeekAsync(TimeSpan.FromSeconds(req.Position));
            return Results.Ok();
        });

        // -------- Playback state --------
        app.MapGet("/playback/state", () =>
        {
            var st = _backend.GetCurrentState();
            return Results.Json(st);
        });

        // -------- Lyrics: parsed lines --------
        app.MapGet("/lyrics", () =>
        {
            var st = _backend.GetCurrentState();
            if (st == null) return Results.NotFound();

            var lines = _lyrics.CurrentLyrics;
            return Results.Json(lines);
        });

        await app.StartAsync(token);
        if (_trust != null)
        {
            _discovery = new LanDiscovery(_lan!, _trust.DeviceId, _lanBindAddress);
            _discoveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            _discoveryTask = _discovery.RunAsync(_discoveryCancellation.Token);
        }
        IsRunning = true;
        app.Lifetime.ApplicationStopped.Register(() => IsRunning = false);
    }

    public async ValueTask DisposeAsync()
    {
        IsRunning = false;
        _discoveryCancellation?.Cancel(); _discovery?.Dispose();
        if (_discoveryTask != null)
            try { await _discoveryTask; }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or System.Net.Sockets.SocketException) { }
        _discoveryCancellation?.Dispose();
        if (_app != null) await _app.DisposeAsync();
        _certificate?.Dispose();
    }

    private sealed class RequestBudget(int limit, TimeSpan period)
    {
        private readonly object _gate = new();
        private long _start;
        private int _count;
        public bool Take()
        {
            lock (_gate)
            {
                var now = Environment.TickCount64;
                if (now - _start >= period.TotalMilliseconds) { _start = now; _count = 0; }
                return ++_count <= limit;
            }
        }
    }

    private sealed class EmbeddedLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
    }
}

public class SeekRequest
{
    [JsonPropertyName("position")]
    public double Position { get; set; }
}
