using System.Globalization;
using System.Text.Json;
using OmniLyrics.Core;

namespace OmniLyrics.Backends.Mac;

/// <summary>Apple Music / Spotify via the system Apple Events bridge, without Homebrew.</summary>
public sealed class MacOSAppleEventsBackend : BasePlayerBackend, IDisposable, IPlayerBackendStatus, ITrackFavorites
{
    public const string MusicBundle = "com.apple.Music";
    public const string SpotifyBundle = "com.spotify.client";
    private readonly string _bundle, _name;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private PlayerState? _state;
    private long _sampleTime;
    private readonly MacPlaybackClock _clock;
    private bool _disposed;
    private TimeSpan? _seekTarget;
    private long _seekTime;
    private string? _errorKey;
    private static readonly string Script = ReadScript();

    public MacOSAppleEventsBackend(string bundle) : this(bundle, TimeProvider.System) { }

    internal MacOSAppleEventsBackend(string bundle, TimeProvider time)
    {
        if (bundle is not (MusicBundle or SpotifyBundle)) throw new ArgumentException("Unsupported Apple Events player.", nameof(bundle));
        _bundle = bundle; _name = bundle == MusicBundle ? "Apple Music" : "Spotify";
        _time = time; _clock = new(time);
    }

    public string? ConnectionError => _errorKey is { } key ? Localization.Format(key, _name) : null;

    private static string ReadScript()
    {
        using var stream = typeof(MacOSAppleEventsBackend).Assembly.GetManifestResourceStream("OmniLyrics.Backends.Mac.Scripts.AppleEvents.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public override PlayerState? GetCurrentState()
    {
        lock (_gate)
        {
            if (_state != null && _time.GetElapsedTime(_sampleTime).TotalSeconds > 5)
            { _errorKey = "MacPlayerUnavailable"; return null; }
            var state = _state?.DeepCopy();
            if (state?.Playing == true)
            {
                state.Position = _clock.Position;
                if (state.Duration > TimeSpan.Zero && state.Position > state.Duration) state.Position = state.Duration;
            }
            return state;
        }
    }

    public override Task StartAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loop != null) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _loop = Task.Run(() => MonitorAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task MonitorAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await MacProcess.StreamAsync(Command("stream"), ProcessLine, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                _errorKey = "MacPlayerUnavailable";
                SetState(null);
                Console.Error.WriteLine($"{_name}: {error.Message}");
            }
            try { await Task.Delay(3000, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal void ProcessLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Null) { _errorKey = null; SetState(null); return; }
            if (root.TryGetProperty("waiting", out _)) { _errorKey = "MacAutomationWaiting"; return; }
            if (root.TryGetProperty("error", out var error))
            {
                var key = error.GetInt32() == -1743 ? "MacAutomationDenied" : "MacPlayerUnavailable";
                if (_errorKey != key) Console.Error.WriteLine(Localization.Format(key, _name));
                _errorKey = key; SetState(null); return;
            }
            var duration = root.GetProperty("duration").GetDouble();
            var position = root.GetProperty("position").GetDouble();
            if (!double.IsFinite(duration) || !double.IsFinite(position) || duration < 0 || position < 0) throw new FormatException("Invalid playback time.");
            var artist = root.GetProperty("artist").GetString();
            var state = new PlayerState
            {
                Title = root.GetProperty("title").GetString(), Album = root.GetProperty("album").GetString(),
                Artists = string.IsNullOrWhiteSpace(artist) ? [] : [artist],
                Duration = TimeSpan.FromSeconds(duration), Position = TimeSpan.FromSeconds(duration > 0 ? Math.Min(position, duration) : position),
                Playing = root.GetProperty("playing").GetBoolean(), SourceApp = _bundle, PlayerName = _name,
                ArtworkUrl = root.TryGetProperty("artworkUrl", out var artwork) ? artwork.GetString() : null
            };
            _errorKey = null;
            SetState(string.IsNullOrWhiteSpace(state.Title) ? null : state);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        { _errorKey = "MacPlayerUnavailable"; SetState(null); }
    }

    private void SetState(PlayerState? state)
    {
        lock (_gate)
        {
            var sameTrack = state != null && _state != null && state.Title == _state.Title && state.Album == _state.Album
                && state.Artists.SequenceEqual(_state.Artists) && state.Duration == _state.Duration;
            if (_seekTarget is { } target && sameTrack && _time.GetElapsedTime(_seekTime).TotalSeconds < 2
                && Math.Abs((state!.Position - target).TotalSeconds) > 1.5) return;
            _seekTarget = null;
            _clock.Update(state?.Position ?? TimeSpan.Zero, state?.Playing == true, reset: !sameTrack);
            _state = state; _sampleTime = _time.GetTimestamp();
        }
        EmitStateChanged(GetCurrentState()!);
    }

    private System.Diagnostics.ProcessStartInfo Command(string action, string? value = null)
    {
        var info = MacProcess.StartInfo("/usr/bin/osascript", "-l", "JavaScript", "-e", Script, "--", _bundle, action);
        if (value != null) info.ArgumentList.Add(value);
        return info;
    }

    private async Task ControlAsync(string action, string? value = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await MacProcess.RunAsync(Command(action, value), _cts?.Token ?? default).ConfigureAwait(false);
    }
    public override Task PlayAsync() => ControlAsync("play");
    public override Task PauseAsync() => ControlAsync("pause");
    public override Task TogglePlayPauseAsync() => ControlAsync("toggle");
    public override Task NextAsync() => ControlAsync("next");
    public override Task PreviousAsync() => ControlAsync("previous");
    public override async Task SeekAsync(TimeSpan position)
    {
        position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        await ControlAsync("seek", position.TotalSeconds.ToString(CultureInfo.InvariantCulture));
        lock (_gate)
        {
            _seekTarget = position; _seekTime = _time.GetTimestamp();
            if (_state != null) _state.Position = position;
            _clock.Update(position, _state?.Playing == true, reset: true);
        }
    }

    public Task<FavoriteState?> GetFavoriteAsync(PlayerState expected, CancellationToken token = default) =>
        FavoriteAsync(expected, null, false, token);
    public Task<FavoriteState?> SetFavoriteAsync(PlayerState expected, FavoriteState previous, bool favorite, CancellationToken token = default) =>
        FavoriteAsync(expected, previous, favorite, token);
    private async Task<FavoriteState?> FavoriteAsync(PlayerState expected, FavoriteState? previous, bool favorite, CancellationToken token)
    {
        if (_disposed || _bundle != MusicBundle) return null;
        var key = OmniLyrics.Core.Shared.LyricsCache.TrackKey(expected);
        if (previous != null && previous.MediaKey != key) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _cts?.Token ?? default);
        try
        {
            var payload = JsonSerializer.Serialize(new { title = expected.Title, artist = string.Join(", ", expected.Artists),
                album = expected.Album, duration = expected.Duration.TotalSeconds, targetId = previous?.TrackId, favorite });
            var output = await MacProcess.CaptureAsync(Command(previous == null ? "favorite-get" : "favorite-set", payload),
                linked.Token, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var id = root.GetProperty("id").GetString();
            var value = root.GetProperty("favorite").GetBoolean();
            if (string.IsNullOrEmpty(id) || previous != null && (id != previous.TrackId || value != favorite)) return null;
            return new(id, key, value);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or JsonException or KeyNotFoundException
            or OperationCanceledException or System.ComponentModel.Win32Exception) { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _loop?.GetAwaiter().GetResult();
        _cts?.Dispose();
    }
}
