using System.Diagnostics;
using OmniLyrics.Core;
using OmniLyrics.Core.Helpers;
using OmniLyrics.Core.Configuration;
using Tmds.DBus;
using Timer = System.Timers.Timer;

namespace OmniLyrics.Backends.Linux;

internal static class MPRISStrings
{
    public const string MprisPrefix = "org.mpris.MediaPlayer2.";
    public const string DBusName = "org.freedesktop.DBus";
    public const string DBusPath = "/org/freedesktop/DBus";
    public const string MediaPlayerPath = "/org/mpris/MediaPlayer2";
    public const string YesPlayMusic = "yesplaymusic";
    public const string Cider = "cider";
}

/// <summary>
///     MPIRS (D-Bus) Backend, only works on Linux
/// </summary>
public class MPRISBackend : BasePlayerBackend, IDisposable, IPlaybackQueueSource
{
    private readonly YesPlayMusicApi _yesPlayMusicApi = new();
    private string? _busName;
    private string? _playerName;
    private PlayerState? _lastState;

    private Player? _player;

    // Timer for periodic polling of the current playback position.
    // This avoids inconsistencies when the user seeks manually.
    private Timer? _pollTimer;
    private IDisposable? _propertyWatcher;
    private IDisposable? _nameWatcher;
    private volatile bool _disposed;
    public bool HasCiderControlSupport { get; private set; }

    public void Dispose()
    {
        _disposed = true;
        _nameWatcher?.Dispose();
        DisconnectPlayer();
    }

    public override PlayerState? GetCurrentState() => _lastState;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var bus = Connection.Session;

        // Watch DBus name changes to detect player appear/disappear
        var dbus = bus.CreateProxy<IDBus>(MPRISStrings.DBusName, MPRISStrings.DBusPath);

        _nameWatcher = await dbus.WatchNameOwnerChangedAsync(args =>
        {
            string name = args.name;
            string oldOwner = args.oldOwner;
            string newOwner = args.newOwner;

            if (!name.StartsWith(MPRISStrings.MprisPrefix))
                return;

            if (!string.IsNullOrEmpty(newOwner) && string.IsNullOrEmpty(oldOwner))
            {
                Debug.WriteLine($"Player appeared: {name}");
                _ = ChooseBestPlayerAsync(cancellationToken);
            }

            if (!string.IsNullOrEmpty(oldOwner) && string.IsNullOrEmpty(newOwner))
            {
                Debug.WriteLine($"Player disappeared: {name}");
                _ = ChooseBestPlayerAsync(cancellationToken);
            }
        });
        if (_disposed) { _nameWatcher.Dispose(); return; }

        // Try connect immediately if already running
        await ChooseBestPlayerAsync(cancellationToken);

        // ----------------------------------------------------------------------
        // Fallback polling in case DBus watch does not fire (some environments)
        // ----------------------------------------------------------------------
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken);
                    await ChooseBestPlayerAsync(cancellationToken);
                }
                catch
                {
                    // ignored
                }
            }
        }, cancellationToken);
    }

    // Pick the best MPRIS player when multiple exist
    private async Task ChooseBestPlayerAsync(CancellationToken token)
    {
        if (_disposed || token.IsCancellationRequested) return;
        var bus = Connection.Session;
        string[]? services = await bus.ListServicesAsync();

        var players = services
            .Where(s => s.StartsWith(MPRISStrings.MprisPrefix))
            .Where(s => !s.Contains(MPRISStrings.Cider, StringComparison.OrdinalIgnoreCase)
                || UserConfiguration.LoadCider().Integration != "webapi")
            .ToList();

        if (players.Count == 0)
        {
            DisconnectPlayer();
            return;
        }

        string? selected = null;

        // 1. Prefer YesPlayMusic
        string? ypm = players.FirstOrDefault(p => p.Contains(MPRISStrings.YesPlayMusic, StringComparison.OrdinalIgnoreCase));
        if (ypm != null)
        {
            selected = ypm;
        }
        else
        {
            // 2. Prefer the one that is playing
            foreach (string p in players)
            {
                try
                {
                    var playerProxy = bus.CreateProxy<IPlayer>(p, MPRISStrings.MediaPlayerPath);
                    var player = new Player(p, playerProxy);
                    string status = await player.GetPlaybackStatusAsync();
                    if (status == "Playing")
                    {
                        selected = p;
                        break;
                    }
                }
                catch { }
            }

            // 3. Fallback: pick first
            selected ??= players[0];
        }

        // ---------------------------------------------------------
        // Avoid unnecessary reconnect if same busName selected
        // ---------------------------------------------------------
        if (selected == _busName)
            return;

        if (!_disposed && !token.IsCancellationRequested) await ConnectToPlayerAsync(selected, token);
    }

    private async Task ConnectToPlayerAsync(string busName, CancellationToken cancellationToken)
    {
        var bus = Connection.Session;

        // Cleanup previous connection
        DisconnectPlayer();

        _busName = busName;
        try
        {
            var application = bus.CreateProxy<IPlayerApplication>(busName, MPRISStrings.MediaPlayerPath);
            _playerName = await application.GetAsync<string>("Identity");
        }
        catch { _playerName = null; }
        if (_disposed || cancellationToken.IsCancellationRequested) return;

        // Create proxy
        var playerProxy = bus.CreateProxy<IPlayer>(_busName, MPRISStrings.MediaPlayerPath);

        // Player wrapper instance
        _player = new Player(_busName, playerProxy);

        // Subscribe to property changes from the Player interface
        _propertyWatcher = await playerProxy.WatchPropertiesAsync(HandlePropertyChanged);
        if (_disposed || cancellationToken.IsCancellationRequested) { DisconnectPlayer(); return; }

        Debug.WriteLine($"[MPRIS] Connected to {_busName}");

        // Start polling of the current playback position
        StartPollingTimer();

        // Initial metadata/status update
        await UpdateStateAsync(null);
    }

    private void DisconnectPlayer()
    {
        _propertyWatcher?.Dispose();
        _propertyWatcher = null;

        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;

        _player = null;
        HasCiderControlSupport = false;
        _busName = null;
        _playerName = null;

        if (_lastState != null)
        {
            _lastState = null;
            EmitStateChanged(null!);
        }
    }

    private async void HandlePropertyChanged(PropertyChanges changes)
    {
        // No interface check is required because the proxy is bound directly to Player.
        await UpdateStateAsync(changes);
    }

    private async Task UpdateStateAsync(PropertyChanges? changes)
    {
        if (_player == null)
            return;

        try
        {
            var meta = await _player.GetMetadataAsync();
            if (meta == null)
                return;

            if (!HasCiderControlSupport && _busName?.Contains(MPRISStrings.Cider, StringComparison.OrdinalIgnoreCase) == true)
            {
                try
                {
                    var proxy = Connection.Session.CreateProxy<IPlayer>(_busName, MPRISStrings.MediaPlayerPath);
                    var properties = new[] { "CanControl", "CanPlay", "CanPause", "CanGoNext", "CanGoPrevious", "CanSeek" };
                    var supported = await Task.WhenAll(properties.Select(p => proxy.GetAsync<bool>(p)));
                    // Navigation/seek can legitimately be false for the current
                    // track. Require the properties to exist, not all to be true.
                    HasCiderControlSupport = supported[0] && !string.IsNullOrEmpty(meta.TrackId);
                }
                catch { HasCiderControlSupport = false; }
            }

            long pos = await _player.GetPositionAsync();
            string status = await _player.GetPlaybackStatusAsync();

            var newState = new PlayerState
            {
                Title = meta.Title,
                Album = meta.Album,
                Position = TimeSpan.FromMicroseconds(pos), // always current real position
                Playing = status == "Playing",
                SourceApp = _busName ?? "Unknown",
                PlayerName = _playerName
            };

            if (meta.Artists != null)
                newState.Artists.AddRange(meta.Artists);

            if (meta.ArtUrl != null)
                newState.ArtworkUrl = meta.ArtUrl.ToString();

            if (meta.Length.HasValue)
                newState.Duration = meta.Length.Value;

            if (!StatesEqual(_lastState, newState) || _lastState?.Position != newState.Position)
            {
                _lastState = newState;
                EmitStateChanged(newState);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static bool StatesEqual(PlayerState? a, PlayerState b)
    {
        if (a is null) return false;

        if (a.Artists.Count != b.Artists.Count)
            return false;

        for (int i = 0; i < a.Artists.Count; i++)
            if (a.Artists[i] != b.Artists[i])
                return false;

        return a.Title == b.Title &&
               a.Album == b.Album &&
               a.Duration == b.Duration &&
               a.Playing == b.Playing &&
               a.SourceApp == b.SourceApp;
    }

    // ---------------------------------------------------------
    // Polling-based position update
    // This retrieves the real position every interval, ensuring
    // correct behavior when the user seeks manually.
    // ---------------------------------------------------------
    private void StartPollingTimer()
    {
        if (_disposed) return;
        _pollTimer = new Timer(200); // 200ms interval
        _pollTimer.AutoReset = true;

        _pollTimer.Elapsed += async (_, _) =>
        {
            var state = _lastState;
            if (_player == null || state == null)
                return;

            try
            {
                long newPos = await _player.GetPositionAsync();
                var posTs = TimeSpan.FromMicroseconds(newPos);

                // Try override position with YesPlayMusic first
                string app = state.SourceApp ?? "";
                if (app.Contains(MPRISStrings.YesPlayMusic, StringComparison.OrdinalIgnoreCase))
                {
                    var ypState = await _yesPlayMusicApi.GetStateAsync();
                    if (ypState != null)
                    {
                        // When YesPlayMusic skipped some songs...
                        if (ypState.Title != state.Title)
                        {
                            state = ypState.DeepCopy();
                        }

                        // Override position anyway
                        state.Position = ypState.Position;
                        EmitStateChanged(state);
                        return;
                    }
                }

                // Fallback: normal MPRIS pos update
                if (posTs != state.Position)
                {
                    state.Position = posTs;
                    EmitStateChanged(state);
                }
            }
            catch
            {
                // ignored
            }
        };

        _pollTimer.Start();
    }

    // ---------------------------
    // Controller commands
    // ---------------------------
    public override Task PlayAsync() => _player?.PlayAsync() ?? Task.CompletedTask;

    public override Task PauseAsync() => _player?.PauseAsync() ?? Task.CompletedTask;

    public override Task TogglePlayPauseAsync() => _player?.PlayPauseAsync() ?? Task.CompletedTask;

    public override Task NextAsync() => _player?.NextAsync() ?? Task.CompletedTask;

    public override Task PreviousAsync() => _player?.PreviousAsync() ?? Task.CompletedTask;

    public override async Task SeekAsync(TimeSpan position)
    {
        var player = _player;
        if (player == null)
            return;

        var metadata = await player.GetMetadataAsync();
        if (!string.IsNullOrEmpty(metadata.TrackId))
            await player.SetPositionAsync(new ObjectPath(metadata.TrackId), (long)position.TotalMicroseconds);
    }

    public async Task<IReadOnlyList<PlayerState>> GetUpcomingTracksAsync(int limit, CancellationToken token = default)
    {
        var name = _busName;
        var player = _player;
        if (name == null || player == null) return [];
        try
        {
            var proxy = Connection.Session.CreateProxy<IPlayerTrackList>(name, MPRISStrings.MediaPlayerPath);
            var tracks = await proxy.GetAsync<ObjectPath[]>("Tracks").WaitAsync(token);
            var current = await player.GetMetadataAsync().WaitAsync(token);
            var index = Array.FindIndex(tracks, id => id.ToString() == current.TrackId);
            if (index < 0) return [];
            var upcoming = tracks.Skip(index + 1).Take(Math.Clamp(limit, 0, 20)).ToArray();
            if (upcoming.Length == 0) return [];
            var metadata = await proxy.GetTracksMetadataAsync(upcoming).WaitAsync(token);
            if (_busName != name) return [];
            return metadata.Select(PlayerMetadata.FromDictionary).Select(track => new PlayerState
            {
                Title = track.Title, Artists = track.Artists?.ToList() ?? [], Album = track.Album,
                Duration = track.Length ?? TimeSpan.Zero, SourceApp = name, PlayerName = _playerName
            }).Where(track => !string.IsNullOrWhiteSpace(track.Title) && track.Artists.Count > 0).ToList();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return []; } // TrackList is optional in MPRIS.
    }
}
