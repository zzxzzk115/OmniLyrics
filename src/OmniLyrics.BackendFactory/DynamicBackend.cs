using OmniLyrics.Backends.CiderV3;
using OmniLyrics.Backends.Linux;
using OmniLyrics.Backends.Mac;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
#if Windows
using OmniLyrics.Backends.Windows;
#endif

namespace OmniLyrics.Backends.Dynamic;

public class DynamicBackend : BasePlayerBackend, IDisposable, IPlaybackQueueSource, ITrackFavorites, IPlayerBackendStatus
{
    private readonly CiderFavorites _favorites = new();
    private readonly Dictionary<string, IPlayerBackend> _backends;

    private readonly bool _mac;
    private readonly object _selectionGate = new();
    private readonly Dictionary<IPlayerBackend, bool> _wasPlaying = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IPlayerBackend, string> _startupErrors = new();
    private Task[] _starts = [];
    private bool _disposed;
    private CancellationTokenSource _cts = new();
    private IPlayerBackend? _current;

    public DynamicBackend() : this(CreateBackends(), OperatingSystem.IsMacOS()) { }

    internal DynamicBackend(Dictionary<string, IPlayerBackend> backends, bool mac)
    {
        _backends = backends; _mac = mac;
        foreach (var backend in _backends.Values) backend.OnStateChanged += HandleSubBackendStateChanged;
        _current = _backends.Values.FirstOrDefault();
    }

    private static Dictionary<string, IPlayerBackend> CreateBackends()
    {
        var backends = new Dictionary<string, IPlayerBackend>();

#if Windows
        if (OperatingSystem.IsWindows())
            backends["SMTC"] = new SMTCBackend();
#endif

        if (OperatingSystem.IsLinux())
            backends["MPRIS"] = new MPRISBackend();

        if (OperatingSystem.IsMacOS())
        {
            backends["AppleMusic"] = new MacOSAppleEventsBackend(MacOSAppleEventsBackend.MusicBundle);
            backends["Spotify"] = new MacOSAppleEventsBackend(MacOSAppleEventsBackend.SpotifyBundle);
            if (MacOSMediaControlBackend.ResolveExecutable() != null)
                backends["MediaControl"] = new MacOSMediaControlBackend();
        }

        backends["CiderV3"] = new CiderV3Backend();
        return backends;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        foreach (var backend in _backends.Values) backend.OnStateChanged -= HandleSubBackendStateChanged;
        try
        {
            foreach (var backend in _backends.Values.OfType<IDisposable>())
                try { backend.Dispose(); }
                catch (Exception error) { Console.Error.WriteLine($"Player cleanup failed: {error.Message}"); }
        }
        finally { _favorites.Dispose(); _cts.Dispose(); }
    }

    public string? ConnectionError => GetCurrentState() != null ? null :
        _backends.Values.OfType<IPlayerBackendStatus>().Select(b => b.ConnectionError).FirstOrDefault(e => e != null)
        ?? _startupErrors.Values.FirstOrDefault();

    public override PlayerState? GetCurrentState()
    {
        if (_mac) { lock (_selectionGate) { SelectMacBackend(); return _current?.GetCurrentState(); } }
        if (_backends.TryGetValue("MPRIS", out var mpris))
        {
            var api = _backends["CiderV3"];
            var mprisState = mpris.GetCurrentState();
            var ciderMpris = mprisState?.SourceApp?.Contains("cider", StringComparison.OrdinalIgnoreCase) == true;
            if (ciderMpris && (_current == mpris || _current == api))
            {
                var integration = UserConfiguration.LoadCider().Integration;
                var preferMpris = integration == "mpris" && mpris is MPRISBackend { HasCiderControlSupport: true };
                _current = !preferMpris && api.GetCurrentState() != null ? api : mpris;
            }
            else if (_current == mpris && mprisState == null && api.GetCurrentState() != null)
                _current = api;
        }
        return _current?.GetCurrentState();
    }

    public override Task StartAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_starts.Length != 0) return Task.CompletedTask;
        _cts.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _starts = _backends.Values.Select(backend => StartBackendAsync(backend, _cts.Token)).ToArray();
        return Task.CompletedTask;
    }

    private async Task StartBackendAsync(IPlayerBackend backend, CancellationToken token)
    {
        try { await backend.StartAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _startupErrors[backend] = Localization.Get("BackendUnavailable");
            Console.Error.WriteLine($"{backend.GetType().Name}: {error.Message}");
        }
    }

    // Prefer a dedicated connection for the same player. Retain the active player
    // between periodic samples; only a new play transition promotes another one.
    private void SelectMacBackend(IPlayerBackend? promoted = null)
    {
        var candidates = _backends.Where(pair => !_startupErrors.ContainsKey(pair.Value))
            .Select(pair => (Key: pair.Key, Backend: pair.Value, State: pair.Value.GetCurrentState()))
            .Where(item => item.State != null).ToList();
        candidates = candidates.Where(item => item.Key != "MediaControl" || !candidates.Any(other =>
            other.Key != "MediaControl" && SameMacPlayer(item.State!, other.State!))).ToList();
        if (promoted != null && candidates.Any(item => item.Backend == promoted && item.State!.Playing)) _current = promoted;
        var active = candidates.FirstOrDefault(item => item.Backend == _current);
        if (active.Backend != null && active.State!.Playing) return;
        var playing = candidates.FirstOrDefault(item => item.State!.Playing);
        _current = playing.Backend ?? active.Backend ?? candidates.FirstOrDefault().Backend;
    }

    private static bool SameMacPlayer(PlayerState a, PlayerState b) =>
        string.Equals(a.SourceApp, b.SourceApp, StringComparison.OrdinalIgnoreCase)
        || a.SourceApp?.Contains("cider", StringComparison.OrdinalIgnoreCase) == true
        && b.SourceApp?.Contains("cider", StringComparison.OrdinalIgnoreCase) == true;

    private void HandleSubBackendStateChanged(object? sender, PlayerState? state)
    {
        if (sender is not IPlayerBackend b)
            return;

        if (_disposed) return;
        if (_mac)
        {
            PlayerState? selected;
            lock (_selectionGate)
            {
                var promoted = state?.Playing == true && !_wasPlaying.GetValueOrDefault(b);
                _wasPlaying[b] = state?.Playing == true;
                SelectMacBackend(promoted ? b : null);
                selected = _current?.GetCurrentState();
            }
            EmitStateChanged(selected!);
            return;
        }
        if (state == null)
        {
            if (_current == b) EmitStateChanged(null!);
            return;
        }

        if (_backends.TryGetValue("MPRIS", out var mpris))
        {
            var integration = UserConfiguration.LoadCider().Integration;
            var mprisHasCider = mpris.GetCurrentState()?.SourceApp?.Contains("cider", StringComparison.OrdinalIgnoreCase) == true;
            var preferMpris = integration == "mpris" && mprisHasCider
                && mpris is MPRISBackend { HasCiderControlSupport: true };
            var webApi = _backends["CiderV3"];
            // V3's partial MPRIS implementation must not displace its Web API.
            // Auto is conservative; V4 users may explicitly prefer capable MPRIS.
            if (b == mpris && mprisHasCider && (integration == "webapi"
                || !preferMpris && webApi.GetCurrentState() != null))
                return;
            if (b == webApi && preferMpris)
                return;
            if (b == webApi && _current == mpris && mprisHasCider)
                _current = webApi;
        }

        // If this backend is now playing, promote it to active backend
        if (state.Playing)
            _current = b;

        if (_current == b)
            EmitStateChanged(state);
    }

    public override Task PlayAsync() => ControlBackend()?.PlayAsync() ?? Task.CompletedTask;
    public override Task PauseAsync() => ControlBackend()?.PauseAsync() ?? Task.CompletedTask;
    public override Task TogglePlayPauseAsync() => ControlBackend()?.TogglePlayPauseAsync() ?? Task.CompletedTask;
    public override Task NextAsync() => ControlBackend()?.NextAsync() ?? Task.CompletedTask;
    public override Task PreviousAsync() => ControlBackend()?.PreviousAsync() ?? Task.CompletedTask;
    private IPlayerBackend? ControlBackend() { GetCurrentState(); return _current; }
    public override Task SeekAsync(TimeSpan pos) => ControlBackend()?.SeekAsync(pos) ?? Task.CompletedTask;

    public async Task<IReadOnlyList<PlayerState>> GetUpcomingTracksAsync(int limit, CancellationToken token = default)
    {
        var state = GetCurrentState();
        if (state == null) return [];
        if (state.SourceApp?.Contains("cider", StringComparison.OrdinalIgnoreCase) == true
            && _backends["CiderV3"] is IPlaybackQueueSource cider)
        {
            var tracks = await cider.GetUpcomingTracksAsync(limit, token);
            if (tracks.Count > 0) return tracks;
        }
        return _current is IPlaybackQueueSource queue
            ? await queue.GetUpcomingTracksAsync(limit, token) : [];
    }

    private ITrackFavorites? FavoritesFor(PlayerState expected)
    {
        var current = GetCurrentState();
        if (current == null || OmniLyrics.Core.Shared.LyricsCache.TrackKey(current) != OmniLyrics.Core.Shared.LyricsCache.TrackKey(expected)) return null;
        return current.SourceApp?.Contains("cider", StringComparison.OrdinalIgnoreCase) == true ? _favorites : _current as ITrackFavorites;
    }

    public Task<FavoriteState?> GetFavoriteAsync(PlayerState expected, CancellationToken token = default) =>
        FavoritesFor(expected)?.GetFavoriteAsync(expected, token) ?? Task.FromResult<FavoriteState?>(null);
    public Task<FavoriteState?> SetFavoriteAsync(PlayerState expected, FavoriteState previous, bool favorite, CancellationToken token = default) =>
        FavoritesFor(expected)?.SetFavoriteAsync(expected, previous, favorite, token) ?? Task.FromResult<FavoriteState?>(null);
}
