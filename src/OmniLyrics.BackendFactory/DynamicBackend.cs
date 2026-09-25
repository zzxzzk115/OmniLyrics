using OmniLyrics.Backends.CiderV3;
using OmniLyrics.Backends.Linux;
using OmniLyrics.Backends.Mac;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
#if Windows
using OmniLyrics.Backends.Windows;
#endif

namespace OmniLyrics.Backends.Dynamic;

public class DynamicBackend : BasePlayerBackend, IDisposable, IPlaybackQueueSource, ITrackFavorites
{
    private readonly CiderFavorites _favorites = new();
    private readonly Dictionary<string, IPlayerBackend> _backends;

    private CancellationTokenSource _cts = new();
    private IPlayerBackend? _current;

    public DynamicBackend()
    {
        _backends = new Dictionary<string, IPlayerBackend>();

#if Windows
        if (OperatingSystem.IsWindows())
            _backends["SMTC"] = new SMTCBackend();
#endif

        if (OperatingSystem.IsLinux())
            _backends["MPRIS"] = new MPRISBackend();

        if (OperatingSystem.IsMacOS())
            _backends["MediaControl"] = new MacOSMediaControlBackend();

        _backends["CiderV3"] = new CiderV3Backend();

        foreach (var kv in _backends)
            kv.Value.OnStateChanged += HandleSubBackendStateChanged;

        _current = _backends.Values.First();
    }

    public void Dispose()
    {
        _favorites.Dispose();
        _cts.Cancel();
        foreach (var backend in _backends.Values.OfType<IDisposable>()) backend.Dispose();
        _cts.Dispose();
    }

    public override PlayerState? GetCurrentState()
    {
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

    public override async Task StartAsync(CancellationToken token)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        foreach (var backend in _backends.Values)
            _ = backend.StartAsync(_cts.Token);

        await Task.CompletedTask;
    }

    private void HandleSubBackendStateChanged(object? sender, PlayerState? state)
    {
        if (sender is not IPlayerBackend b)
            return;

        if (state == null)
            return;

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

    public override Task PlayAsync() => _current?.PlayAsync() ?? Task.CompletedTask;
    public override Task PauseAsync() => _current?.PauseAsync() ?? Task.CompletedTask;
    public override Task TogglePlayPauseAsync() => _current?.TogglePlayPauseAsync() ?? Task.CompletedTask;
    public override Task NextAsync() => _current?.NextAsync() ?? Task.CompletedTask;
    public override Task PreviousAsync() => _current?.PreviousAsync() ?? Task.CompletedTask;
    public override Task SeekAsync(TimeSpan pos) => _current?.SeekAsync(pos) ?? Task.CompletedTask;

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
