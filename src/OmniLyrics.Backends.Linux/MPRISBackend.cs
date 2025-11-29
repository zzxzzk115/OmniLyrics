using OmniLyrics.Core;
using Tmds.DBus;

namespace OmniLyrics.Backends.Linux;

/// <summary>
/// MPIRS (D-Bus) Backend, only works on Linux
/// </summary>
public class MPRISBackend : BasePlayerBackend
{
    private PlayerState? _lastState;

    private Player? _player;
    private string? _busName;
    private IDisposable? _propertyWatcher;

    // Timer for periodic polling of the current playback position.
    // This avoids inconsistencies when the user seeks manually.
    private System.Timers.Timer? _pollTimer;

    public override PlayerState? GetCurrentState() => _lastState;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var bus = Connection.Session;

        // Watch DBus name changes to detect player appear/disappear
        var dbus = bus.CreateProxy<IDBus>("org.freedesktop.DBus", "/org/freedesktop/DBus");

        await dbus.WatchNameOwnerChangedAsync(args =>
        {
            var name = args.name;
            var oldOwner = args.oldOwner;
            var newOwner = args.newOwner;

            if (!name.StartsWith("org.mpris.MediaPlayer2."))
                return;

            if (!string.IsNullOrEmpty(newOwner) && string.IsNullOrEmpty(oldOwner))
            {
                Console.WriteLine($"Player appeared: {name}");
                _ = ConnectToPlayerAsync(name, cancellationToken);
            }

            if (!string.IsNullOrEmpty(oldOwner) && string.IsNullOrEmpty(newOwner))
            {
                Console.WriteLine($"Player disappeared: {name}");
                if (_busName == name)
                    DisconnectPlayer();
            }
        });

        // Try connect immediately if already running
        var services = await bus.ListServicesAsync();
        var existing = services.FirstOrDefault(n => n.StartsWith("org.mpris.MediaPlayer2."));
        if (existing != null)
        {
            await ConnectToPlayerAsync(existing, cancellationToken);
        }
    }

    private async Task ConnectToPlayerAsync(string busName, CancellationToken cancellationToken)
    {
        var bus = Connection.Session;

        // Cleanup previous connection
        DisconnectPlayer();

        _busName = busName;

        // Create proxy
        var playerProxy = bus.CreateProxy<IPlayer>(_busName, "/org/mpris/MediaPlayer2");

        // Player wrapper instance
        _player = new Player(_busName, playerProxy);

        // Subscribe to property changes from the Player interface
        _propertyWatcher = await playerProxy.WatchPropertiesAsync(HandlePropertyChanged);

        Console.WriteLine($"[MPRIS] Connected to {_busName}");

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
        _busName = null;

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

            var pos = await _player.GetPositionAsync();
            var status = await _player.GetPlaybackStatusAsync();

            var newState = new PlayerState
            {
                Title = meta.Title,
                Album = meta.Album,
                Position = TimeSpan.FromMicroseconds(pos), // always current real position
                Playing = status == "Playing",
                SourceApp = _busName ?? "Unknown"
            };

            if (meta.Artists != null)
                newState.Artists.AddRange(meta.Artists);

            if (meta.ArtUrl != null)
                newState.ArtworkUrl = meta.ArtUrl.ToString();

            if (meta.Length.HasValue)
                newState.Duration = meta.Length.Value;

            if (!StatesEqual(_lastState, newState))
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
        _pollTimer = new System.Timers.Timer(200); // 200ms interval
        _pollTimer.AutoReset = true;

        _pollTimer.Elapsed += async (_, _) =>
        {
            var state = _lastState;
            if (_player == null || state == null || !state.Playing)
                return;

            try
            {
                var newPos = await _player.GetPositionAsync();
                var posTs = TimeSpan.FromMicroseconds(newPos);

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

    public override Task SeekAsync(TimeSpan position)
        => _player?.SetPositionAsync("/", (long)position.TotalMicroseconds) ?? Task.CompletedTask;
}