using System.Text.Json.Serialization;
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
    public bool IsRunning { get; private set; }

    public WebApiServer(IPlayerBackend backend, ILyricsProvider lyrics, int port = ClientServerCommonDefine.WebApiPort,
        string listenAddress = "127.0.0.1", LyricsManager? karaoke = null, string? ownerRole = null)
    {
        _backend = backend;
        _lyrics = lyrics;
        _port = port;
        _listenAddress = System.Net.IPAddress.Parse(listenAddress).ToString();
        _karaoke = karaoke ?? new LyricsManager();
        _ownerRole = ownerRole;
    }

    public async Task StartAsync(CancellationToken token)
    {
        var builder = WebApplication.CreateBuilder();

        // A hosting widget keeps stdout exclusively for its JSON protocol.
        builder.Logging.ClearProviders();
        // The owning frontend controls lifetime; Kestrel must not install its
        // own process signal handlers inside a GUI or widget process.
        builder.Services.AddSingleton<IHostLifetime, EmbeddedLifetime>();

        var host = _listenAddress.Contains(':') ? $"[{_listenAddress}]" : _listenAddress;
        builder.WebHost.UseUrls($"http://{host}:{_port}");

        var app = builder.Build();
        _app = app;

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
            await _backend.SeekAsync(TimeSpan.FromSeconds(req.Position));
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
        IsRunning = true;
        app.Lifetime.ApplicationStopped.Register(() => IsRunning = false);
    }

    public async ValueTask DisposeAsync()
    {
        IsRunning = false;
        if (_app != null) await _app.DisposeAsync();
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
