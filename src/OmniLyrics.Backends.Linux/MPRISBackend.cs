using System.Diagnostics;
using OmniLyrics.Core;
using OmniLyrics.Core.Helpers;
using Tmds.DBus;

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
/// MPIRS (D-Bus) Backend, only works on Linux
/// </summary>
public class MPRISBackend : BasePlayerBackend
{
    private PlayerState? _lastState;

    private Player? _player;
    private string? _busName;
    private IDisposable? _propertyWatcher;

    private YesPlayMusicApi _yesPlayMusicApi = new();

    // Timer for periodic polling of the current playback position.
    // This avoids inconsistencies when the user seeks manually.
    private System.Timers.Timer? _pollTimer;

    public override PlayerState? GetCurrentState() => _lastState;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var bus = Connection.Session;

        // Watch DBus name changes to detect player appear/disappear
        var dbus = bus.CreateProxy<IDBus>(MPRISStrings.DBusName, MPRISStrings.DBusPath);

        await dbus.WatchNameOwnerChangedAsync(args =>
        {
            var name = args.name;
            var oldOwner = args.oldOwner;
            var newOwner = args.newOwner;

            if (!name.StartsWith(MPRISStrings.MprisPrefix))
                return;

            // Skip Cider always
            if (name.Contains(MPRISStrings.Cider, StringComparison.OrdinalIgnoreCase))
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
        var bus = Connection.Session;
        var services = await bus.ListServicesAsync();

        var players = services
            .Where(s => s.StartsWith(MPRISStrings.MprisPrefix)
                     && !s.Contains(MPRISStrings.Cider, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (players.Count == 0)
        {
            DisconnectPlayer();
            return;
        }

        string? selected = null;

        // 1. Prefer YesPlayMusic
        var ypm = players.FirstOrDefault(p => p.Contains(MPRISStrings.YesPlayMusic, StringComparison.OrdinalIgnoreCase));
        if (ypm != null)
        {
            selected = ypm;
        }
        else
        {
            // 2. Prefer the one that is playing
            foreach (var p in players)
            {
                try
                {
                    var playerProxy = bus.CreateProxy<IPlayer>(p, MPRISStrings.MediaPlayerPath);
                    var player = new Player(p, playerProxy);
                    var status = await player.GetPlaybackStatusAsync();
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

        await ConnectToPlayerAsync(selected, token);
    }

    private async Task ConnectToPlayerAsync(string busName, CancellationToken cancellationToken)
    {
        // Skip Cider (handled by Cider backend)
        if (busName.Contains(MPRISStrings.Cider, StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[MPRIS] Skipped Cider: {busName}");
            return;
        }

        var bus = Connection.Session;

        // Cleanup previous connection
        DisconnectPlayer();

        _busName = busName;

        // Create proxy
        var playerProxy = bus.CreateProxy<IPlayer>(_busName, MPRISStrings.MediaPlayerPath);

        // Player wrapper instance
        _player = new Player(_busName, playerProxy);

        // Subscribe to property changes from the Player interface
        _propertyWatcher = await playerProxy.WatchPropertiesAsync(HandlePropertyChanged);

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

                // Try override position with YesPlayMusic first
                var app = state.SourceApp ?? "";
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

    public override Task SeekAsync(TimeSpan position)
        => _player?.SetPositionAsync("/", (long)position.TotalMicroseconds) ?? Task.CompletedTask;
}