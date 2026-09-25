using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OmniLyrics.Core;

namespace OmniLyrics.Web;

public class WebApiServer
{
    private readonly IPlayerBackend _backend;
    private readonly ILyricsProvider _lyrics;
    private readonly int _port;

    public WebApiServer(IPlayerBackend backend, ILyricsProvider lyrics, int port = ClientServerCommonDefine.WebApiPort)
    {
        _backend = backend;
        _lyrics = lyrics;
        _port = port;
    }

    public async Task StartAsync(CancellationToken token)
    {
        var builder = WebApplication.CreateBuilder();

#if DEBUG
        builder.Logging.AddFilter("Microsoft", LogLevel.Information);
        builder.Logging.AddFilter("System", LogLevel.Information);
#else
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);
#endif

        builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");

        var app = builder.Build();

        // --------  Playback control routes  --------
        app.MapPost("/playback/play", () => _backend.PlayAsync());
        app.MapPost("/playback/pause", () => _backend.PauseAsync());
        app.MapPost("/playback/toggle", () => _backend.TogglePlayPauseAsync());
        app.MapPost("/playback/next", () => _backend.NextAsync());
        app.MapPost("/playback/prev", () => _backend.PreviousAsync());

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
    }
}

public class SeekRequest
{
    [JsonPropertyName("position")]
    public double Position { get; set; }
}