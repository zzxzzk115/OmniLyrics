using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OmniLyrics.Backends.Dynamic;
using OmniLyrics.Core;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Shared;
using OmniLyrics.Web;

var passed = 0;
void Check(string name, bool ok)
{
    if (!ok) throw new Exception(name);
    Console.WriteLine("PASS " + name); passed++;
}
async Task Until(Func<bool> predicate)
{
    for (var i = 0; i < 100; i++) { if (predicate()) return; await Task.Delay(50); }
    throw new Exception("Condition timed out");
}
foreach (object value in new object[] { 210026000L, 210026000UL, 210026000, 210026000U })
{
    var metadata = OmniLyrics.Backends.Linux.PlayerMetadata.FromDictionary(new Dictionary<string, object>
        { ["mpris:length"] = value, ["xesam:title"] = "Spotify sample" });
    Check($"MPRIS duration accepts {value.GetType().Name}", metadata.Length == TimeSpan.FromMilliseconds(210026));
}
foreach (object value in new object[] { -1L, -1, ulong.MaxValue, long.MaxValue, "210026000" })
{
    var metadata = OmniLyrics.Backends.Linux.PlayerMetadata.FromDictionary(new Dictionary<string, object>
        { ["mpris:length"] = value, ["xesam:title"] = "Spotify sample" });
    Check($"Invalid MPRIS duration preserves track metadata: {value.GetType().Name} {value}",
        metadata.Length == null && metadata.Title == "Spotify sample");
}
Check("Missing duration does not downgrade artist and album metadata", MediaTypeDetector.Guess(new()
    { Title = "Spotify sample", Artists = ["Artist"], Album = "Album" }) == MediaType.Music);
Check("A known short clip still keeps its duration evidence", MediaTypeDetector.Guess(new()
    { Title = "Short clip", Duration = TimeSpan.FromSeconds(1) }) == MediaType.Video);
foreach (var ip in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
{
    using var receiver = new UdpClient(new IPEndPoint(ip, 0));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await ControlSender.SendAsync("pause", ip.ToString(), ((IPEndPoint)receiver.Client.LocalEndPoint!).Port);
    var packet = await receiver.ReceiveAsync(timeout.Token);
    Check($"UDP control supports {ip.AddressFamily} destinations", System.Text.Encoding.UTF8.GetString(packet.Buffer) == "pause");
}
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var address = new Uri($"http://127.0.0.1:{port}/");
var backend = new FakeBackend();
var delayed = new TaskCompletionSource<List<LyricsLine>?>(TaskCreationOptions.RunContinuationsAsynchronously);
var loaded = 0;
List<LyricsLine> Lines(string text) => [new(TimeSpan.Zero, text, [new(TimeSpan.Zero, TimeSpan.FromSeconds(1), text)], new() { ["zh"] = "示例译文" })];
var manager = new LyricsManager((state, karaoke) =>
{
    Interlocked.Increment(ref loaded);
    if (!karaoke) throw new Exception("GUI snapshot must request timed lyrics");
    return state.Title == "Slow song" ? delayed.Task : Task.FromResult<List<LyricsLine>?>(Lines(state.Title!));
});
WebApiServer Server() => new(backend, new EmptyLyrics(), port, karaoke: manager);
var server = Server();
await server.StartAsync(default);
var locals = new List<FakeBackend>();
using var session = new DesktopSession(() => { var local = new FakeBackend(); locals.Add(local); return local; }, () => address);
await session.StartAsync(default);
await Until(() => session.RemoteSnapshot?.Lyrics is { Count: > 0 });
Check("GUI attaches to existing CLI without starting another player backend", locals.Count == 0);
Check("Shared snapshot carries real syllable duration", session.RemoteSnapshot!.Lyrics![0].Tokens![0].Duration == TimeSpan.FromSeconds(1));
Check("GUI and CLI share the same track", session.GetCurrentState()?.Title == backend.State!.Title);
Check("Shared snapshots preserve bilingual lyrics", session.RemoteSnapshot!.Lyrics![0].Translation == "示例译文");
var favorite = await session.GetFavoriteAsync(backend.State!.DeepCopy());
Check("CLI exposes optional favorite status without mutation", favorite != null && !favorite.IsFavorite && backend.FavoriteWrites == 0);
var confirmed = await session.SetFavoriteAsync(backend.State.DeepCopy(), favorite!, true);
Check("GUI favorites use the existing CLI protocol", confirmed?.IsFavorite == true && backend.FavoriteWrites == 1 && locals.Count == 0);
var staleFavorite = favorite! with { MediaKey = "stale-track" };
Check("Stale favorite requests never reach the backend", await session.SetFavoriteAsync(backend.State.DeepCopy(), staleFavorite, true) == null && backend.FavoriteWrites == 1);
backend.SupportsFavorites = false;
Check("Unsupported remote favorite capability is absent", await session.GetFavoriteAsync(backend.State.DeepCopy()) == null);
backend.SupportsFavorites = true;
var initialLyrics = session.RemoteSnapshot.Lyrics;
await Task.Delay(350);
Check("Polling reuses unchanged lyrics and does not refetch provider data", loaded == 1 && ReferenceEquals(initialLyrics, session.RemoteSnapshot.Lyrics));
await session.PauseAsync(); await Until(() => session.GetCurrentState()?.Playing == false);
Check("GUI pause goes through CLI protocol", backend.Pauses == 1 && locals.Count == 0);
await session.SeekAsync(TimeSpan.FromSeconds(12.5)); await Until(() => session.GetCurrentState()?.Position.TotalSeconds == 12.5);
Check("GUI seek goes through CLI protocol while paused", backend.Seeks == 1);
await session.NextAsync();
Check("GUI navigation reaches only the CLI backend", backend.Nexts == 1);
backend.FailNext = true;
await session.NextAsync();
Check("Failed controls show an error and are never replayed locally", backend.Nexts == 2 && locals.Count == 0 && session.LastControlError != null);
backend.FailNext = false;
await session.PlayAsync(); await Until(() => session.GetCurrentState()?.Playing == true);
Check("Successful control clears the previous error", session.LastControlError == null);
backend.State!.Title = "Slow song";
await Until(() => session.RemoteSnapshot is { Loading: true, Lyrics: null });
Check("New track clears previous shared lyrics while loading", session.GetCurrentState()?.Title == "Slow song");
backend.State.Title = "Latest song";
await Until(() => session.RemoteSnapshot?.Lyrics?[0].Text == "Latest song");
delayed.SetResult(Lines("Stale song")); await Task.Delay(250);
Check("Late lyric responses cannot replace the current shared song", session.RemoteSnapshot!.Lyrics![0].Text == "Latest song");
backend.State = null; await Until(() => session.RemoteSnapshot?.State == null);
Check("No player does not make GUI abandon a healthy CLI service", session.RemoteSnapshot != null && locals.Count == 0);
await server.DisposeAsync();
await Until(() => locals.Count == 1);
Check("CLI exit activates a direct backend", session.RemoteSnapshot == null && locals[0].Started);
await session.PauseAsync();
Check("Standalone GUI controls the direct backend", locals[0].Pauses == 1);
backend.State = FakeBackend.NewState();
server = Server(); await server.StartAsync(default);
await Until(() => session.RemoteSnapshot != null);
Check("Starting CLI after GUI automatically reconnects", locals.Count == 1 && locals[0].Disposed && locals[0].Cancelled);
await server.DisposeAsync();
await Until(() => session.RemoteSnapshot == null);

// A different application at the configured port is not a service to control.
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders(); builder.WebHost.UseUrls(address.ToString());
await using var foreign = builder.Build();
var wrongVersion = false; var foreignWrites = 0;
foreign.MapGet("/snapshot", () => Results.Json(new LyricsSnapshot(wrongVersion ? "OmniLyrics" : "DifferentApp", 99, backend.State, null, false, "test")));
foreign.MapPost("/playback/{action}", () => { foreignWrites++; return Results.Ok(); });
await foreign.StartAsync();
await Task.Delay(1200); await session.NextAsync();
Check("Occupied foreign port is ignored and receives no controls", session.RemoteSnapshot == null && foreignWrites == 0);
wrongVersion = true; await Task.Delay(1200);
Check("Unsupported protocol version is ignored", session.RemoteSnapshot == null);
session.Dispose();
Check("Shutdown stops the owned direct backend", locals.All(local => local.Disposed && local.Cancelled));
Console.WriteLine($"{passed} service checks passed");

sealed class EmptyLyrics : ILyricsProvider { public List<LyricsLine>? CurrentLyrics => null; }
sealed class FakeBackend : BasePlayerBackend, IDisposable, ITrackFavorites
{
    public bool SupportsFavorites = true, Favorite;
    public int FavoriteWrites;
    public Task<FavoriteState?> GetFavoriteAsync(PlayerState expected, CancellationToken token = default) => Task.FromResult<FavoriteState?>(
        SupportsFavorites ? new("fake-id", LyricsCache.TrackKey(expected), Favorite) : null);
    public Task<FavoriteState?> SetFavoriteAsync(PlayerState expected, FavoriteState previous, bool favorite, CancellationToken token = default)
    { FavoriteWrites++; Favorite = favorite; return Task.FromResult<FavoriteState?>(previous with { IsFavorite = favorite }); }
    public static PlayerState NewState() => new() { Title = "Original song", Artists = ["Test artist"], Album = "Test", Duration = TimeSpan.FromMinutes(3), Playing = true, SourceApp = "Mock" };
    public PlayerState? State = NewState();
    public int Pauses, Seeks, Nexts;
    public bool Started, Disposed, Cancelled, FailNext;
    private CancellationToken _token;
    public override PlayerState? GetCurrentState() => State;
    public override Task StartAsync(CancellationToken token) { Started = true; _token = token; return Task.CompletedTask; }
    public override Task PlayAsync() { State!.Playing = true; return Task.CompletedTask; }
    public override Task PauseAsync() { Pauses++; State!.Playing = false; return Task.CompletedTask; }
    public override Task TogglePlayPauseAsync() { State!.Playing = !State.Playing; return Task.CompletedTask; }
    public override Task NextAsync() { Nexts++; if (FailNext) throw new InvalidOperationException("Simulated failed response after receipt"); return Task.CompletedTask; }
    public override Task PreviousAsync() => Task.CompletedTask;
    public override Task SeekAsync(TimeSpan position) { Seeks++; State!.Position = position; return Task.CompletedTask; }
    public void Dispose() { Disposed = true; Cancelled = _token.IsCancellationRequested; }
}
