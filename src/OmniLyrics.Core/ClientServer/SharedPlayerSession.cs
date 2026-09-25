using System.Net;
using System.Net.NetworkInformation;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Shared;
using OmniLyrics.Web;

namespace OmniLyrics.Core;

/// <summary>All frontends attach first and elect a single local service owner when no service remains.</summary>
public class SharedPlayerSession : BasePlayerBackend, IDisposable, IAsyncDisposable, IPlaybackQueueSource, ITrackFavorites
{
    private readonly Func<IPlayerBackend> _createLocal;
    private readonly Func<ServerSettings> _settings;
    private readonly Func<Uri>? _serviceAddress;
    private readonly ServiceRole _role;
    private readonly bool _hostService;
    private readonly LyricsServiceClient _client = new();
    private readonly object _gate = new();
    private IPlayerBackend? _local;
    private CancellationTokenSource? _localCancellation, _cancellation;
    private volatile RemoteFrame? _remote;
    // Keep remote revisions distinct from the local manager's non-negative revisions.
    private long _remoteRevision = long.MinValue;
    private Task? _run, _udpTask;
    private WebApiServer? _web;
    private CommandServer? _udp;
    private FileStream? _lease;
    private LyricsPrefetcher? _prefetch;
    private bool _disposed;
    private volatile bool _ownsService;
    private sealed record RemoteFrame(Uri Address, LyricsSnapshot Snapshot, long Revision);

    public SharedPlayerSession(Func<IPlayerBackend> createLocal, ServiceRole role,
        Func<ServerSettings>? settings = null, Func<Uri>? serviceAddress = null,
        LyricsManager? lyrics = null, bool hostService = true)
    {
        _createLocal = createLocal;
        _role = role;
        _settings = settings ?? UserConfiguration.LoadServer;
        _serviceAddress = serviceAddress;
        _hostService = hostService;
        Lyrics = lyrics ?? new LyricsManager();
    }

    public LyricsManager Lyrics { get; }
    public bool OwnsService => _ownsService;
    public ServiceRole Role => _role;
    public LyricsSnapshot? RemoteSnapshot => _remote?.Snapshot;
    public string? LastControlError { get; private set; }
    public string? ServiceError { get; private set; }
    public override PlayerState? GetCurrentState()
    {
        var remote = _remote;
        if (remote != null) return remote.Snapshot.State;
        lock (_gate) return _local?.GetCurrentState();
    }

    public (List<LyricsLine>? Lines, bool Loading, long Revision) CaptureLyrics(PlayerState? state)
    {
        var remote = _remote;
        if (remote == null) return Lyrics.Capture(state);
        return state != null && remote.Snapshot.State != null
            && LyricsCache.TrackKey(state) == LyricsCache.TrackKey(remote.Snapshot.State)
            ? (remote.Snapshot.Lyrics, remote.Snapshot.Loading, remote.Revision)
            : (null, false, remote.Revision);
    }

    public override Task StartAsync(CancellationToken token)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run != null) return Task.CompletedTask;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            _run = Task.Run(() => RunAsync(_cancellation.Token), CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    private static bool IsLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var address)) return false;
        return IPAddress.IsLoopback(address) || NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Any(a => a.Address.Equals(address));
    }

    private async Task RunAsync(CancellationToken token)
    {
        ServiceElection? election = null;
        ServerSettings? active = null;
        long unavailableSince = 0, retryAt = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var settings = _settings();
                    UserConfiguration.ValidateServer(settings);
                    if (settings != active)
                    {
                        await StopOwnedAsync();
                        election?.Dispose(); election = null;
                        active = settings;
                        if (_hostService && IsLocalHost(settings.ControlHost)) election = new(settings, _role);
                        unavailableSince = 0;
                    }
                    election?.Heartbeat();
                    if (_ownsService)
                    {
                        if (_web?.IsRunning != true || _udpTask?.IsCompleted == true)
                        {
                            await StopOwnedAsync();
                            unavailableSince = 0;
                        }
                        else
                        {
                            ServiceError = null;
                            _ = Lyrics.UpdateAsync(GetCurrentState(), true);
                            await Task.Delay(100, token);
                            continue;
                        }
                    }
                    var address = _serviceAddress?.Invoke() ?? new UriBuilder("http", settings.ControlHost, settings.HttpPort).Uri;
                    var snapshot = await _client.TryReadAsync(address, token);
                    if (token.IsCancellationRequested) break;
                    if (snapshot != null)
                    {
                        var newlyConnected = _remote == null;
                        StopLocal();
                        var revision = snapshot.LyricsVersion == _remote?.Snapshot.LyricsVersion
                            ? _remote.Revision : Interlocked.Increment(ref _remoteRevision);
                        _remote = new(address, snapshot, revision);
                        unavailableSince = 0;
                        ServiceError = null;
                        if (newlyConnected) Console.Error.WriteLine("Connected to existing OmniLyrics service.");
                        EmitStateChanged(snapshot.State!);
                    }
                    else
                    {
                        _remote = null;
                        if (!_hostService)
                        {
                            // Explicitly injected standalone mode is used by UI tests and embedders.
                            if (_local == null) await StartLocalAsync(token);
                        }
                        else if (election != null)
                        {
                            if (unavailableSince == 0) unavailableSince = Environment.TickCount64;
                            // Give all concurrently starting frontends time to register before ranking them.
                            if (Environment.TickCount64 - unavailableSince >= 650 && Environment.TickCount64 >= retryAt)
                            {
                                _lease = election.TryAcquire();
                                if (_lease != null)
                                {
                                    // A legacy or concurrently binding service may have appeared meanwhile.
                                    snapshot = await _client.TryReadAsync(address, token);
                                    if (snapshot != null) { _lease.Dispose(); _lease = null; }
                                    else
                                    {
                                        try { await StartOwnedAsync(settings, token); ServiceError = null; }
                                        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
                                        {
                                            await StopOwnedAsync();
                                            ReportServiceError();
                                            retryAt = Environment.TickCount64 + 2000;
                                        }
                                    }
                                }
                            }
                        }
                        else ReportServiceError();
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
                { ReportServiceError(); }
                await Task.Delay(_remote?.Snapshot.State?.Playing == true ? 100 : 250, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { await StopOwnedAsync(); election?.Dispose(); _remote = null; }
    }

    private void ReportServiceError()
    {
        var message = Localization.Get("ServiceUnavailable");
        if (ServiceError != message) Console.Error.WriteLine(message);
        ServiceError = message;
    }

    private async Task StartLocalAsync(CancellationToken token)
    {
        _localCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (_gate) _local = _createLocal();
        _local.OnStateChanged += ForwardLocal;
        await _local.StartAsync(_localCancellation.Token);
    }

    private async Task StartOwnedAsync(ServerSettings settings, CancellationToken token)
    {
        _localCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (_gate) _local = _createLocal();
        // Bind BOTH protocols before connecting to players. A foreign listener is never interrupted.
        _udp = new CommandServer(_local, settings.ListenAddress, settings.UdpPort);
        _web = new WebApiServer(_local, new SessionLyrics(this), settings.HttpPort, settings.ListenAddress, Lyrics, _role.ToString().ToLowerInvariant());
        await _web.StartAsync(token);
        _local.OnStateChanged += ForwardLocal;
        await _local.StartAsync(_localCancellation.Token);
        _udpTask = _udp.StartAsync(_localCancellation.Token);
        _prefetch = new LyricsPrefetcher(_local, Lyrics, _localCancellation.Token);
        _ownsService = true;
        Console.Error.WriteLine($"OmniLyrics service owner: {_role}.");
    }

    private sealed class SessionLyrics(SharedPlayerSession session) : ILyricsProvider
    {
        public List<LyricsLine>? CurrentLyrics => session.Lyrics.Capture(session.GetCurrentState()).Lines;
    }

    private void ForwardLocal(object? sender, PlayerState state)
    {
        if (_remote == null && !_disposed) EmitStateChanged(state);
    }

    private void StopLocal()
    {
        lock (_gate)
        {
            _localCancellation?.Cancel();
            _prefetch?.Dispose(); _prefetch = null;
            if (_local != null)
            {
                _local.OnStateChanged -= ForwardLocal;
                (_local as IDisposable)?.Dispose();
            }
            _localCancellation?.Dispose();
            _localCancellation = null; _local = null;
        }
    }

    private async Task StopOwnedAsync()
    {
        _ownsService = false;
        _localCancellation?.Cancel();
        _udp?.Dispose(); _udp = null;
        if (_udpTask != null)
        {
            try { await _udpTask; }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or System.Net.Sockets.SocketException) { }
            _udpTask = null;
        }
        if (_web != null) { await _web.DisposeAsync(); _web = null; }
        StopLocal();
        _lease?.Dispose(); _lease = null;
    }
    private async Task ControlAsync(string action, Func<IPlayerBackend, Task> localAction, TimeSpan? position = null)
    {
        LastControlError = null;
        var remote = _remote;
        try
        {
            if (remote != null)
                await _client.ControlAsync(remote.Address, action, position, _cancellation?.Token ?? default);
            else
            {
                IPlayerBackend? local;
                lock (_gate) local = _local;
                if (local != null) await localAction(local);
            }
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            // Never replay a failed toggle/next command against a different backend.
            LastControlError = Localization.Text("Could not send playback command. Please try again.");
        }
    }

    public override Task PlayAsync() => ControlAsync("play", b => b.PlayAsync());
    public override Task PauseAsync() => ControlAsync("pause", b => b.PauseAsync());
    public override Task TogglePlayPauseAsync() => ControlAsync("toggle", b => b.TogglePlayPauseAsync());
    public override Task NextAsync() => ControlAsync("next", b => b.NextAsync());
    public override Task PreviousAsync() => ControlAsync("prev", b => b.PreviousAsync());
    public override Task SeekAsync(TimeSpan pos) => ControlAsync("seek", b => b.SeekAsync(pos), pos);

    public Task<IReadOnlyList<PlayerState>> GetUpcomingTracksAsync(int limit, CancellationToken token = default)
    {
        lock (_gate) return _remote == null && _local is IPlaybackQueueSource queue
            ? queue.GetUpcomingTracksAsync(limit, token) : Task.FromResult<IReadOnlyList<PlayerState>>([]);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        Task? run;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cancellation?.Cancel();
            run = _run;
        }
        if (run != null) await run.ConfigureAwait(false);
        _client.Dispose(); _cancellation?.Dispose();
    }

    public async Task<FavoriteState?> GetFavoriteAsync(PlayerState expected, CancellationToken token = default)
    {
        var remote = _remote;
        IPlayerBackend? local;
        lock (_gate) local = _local;
        var result = remote != null ? await _client.GetFavoriteAsync(remote.Address, token)
            : local is ITrackFavorites favorites ? await favorites.GetFavoriteAsync(expected, token) : null;
        return result?.MediaKey == OmniLyrics.Core.Shared.LyricsCache.TrackKey(expected) ? result : null;
    }

    public async Task<FavoriteState?> SetFavoriteAsync(PlayerState expected, FavoriteState previous, bool favorite, CancellationToken token = default)
    {
        if (previous.MediaKey != OmniLyrics.Core.Shared.LyricsCache.TrackKey(expected)) return null;
        var remote = _remote;
        IPlayerBackend? local;
        lock (_gate) local = _local;
        return remote != null ? await _client.SetFavoriteAsync(remote.Address, previous, favorite, token)
            : local is ITrackFavorites favorites ? await favorites.SetFavoriteAsync(expected, previous, favorite, token) : null;
    }
}
