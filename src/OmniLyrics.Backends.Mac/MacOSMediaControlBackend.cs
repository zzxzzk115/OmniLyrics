using System.Text.Json;
using System.Globalization;
using OmniLyrics.Core;
using Timer = System.Timers.Timer;

namespace OmniLyrics.Backends.Mac;

public class MacOSMediaControlBackend : BasePlayerBackend, IDisposable, IPlayerBackendStatus
{
    private readonly string _executable;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;
    public string? ConnectionError { get; private set; }

    public MacOSMediaControlBackend() : this(ResolveExecutable() ?? "media-control") { }
    internal MacOSMediaControlBackend(string executable) => _executable = executable;

    public static string? ResolveExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("OMNILYRICS_MEDIA_CONTROL");
        if (string.Equals(configured, "off", StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        // GUI launches do not necessarily inherit a shell's Homebrew PATH.
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Concat(new[] { "/opt/homebrew/bin", "/usr/local/bin" });
        return directories.Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => Path.Combine(d, "media-control")).FirstOrDefault(File.Exists);
    }

    private long _lastElapsedMicros;
    private PlayerState? _lastState;
    private long _lastTickMicros;

    // timestamp / elapsed provided by stream
    private long _lastTimestampMicros;
    private bool _playing;

    // timer for incremental position updates
    private Timer? _posTimer;
    private Task? _streamLoop;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _posTimer?.Dispose();
        _streamLoop?.GetAwaiter().GetResult();
        _cts?.Dispose();
    }

    public override PlayerState? GetCurrentState() { lock (_gate) return _lastState?.DeepCopy(); }

    public override Task StartAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_streamLoop != null) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _streamLoop = Task.Run(() => StreamLoopAsync(_cts.Token), CancellationToken.None);
        StartPositionTimer();
        return Task.CompletedTask;
    }

    private async Task StreamLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await MacProcess.StreamAsync(MacProcess.StartInfo(_executable, "stream", "--micros"), line =>
                {
                    ConnectionError = null;
                    ProcessJsonLine(line);
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                if (ConnectionError == null) Console.Error.WriteLine($"media-control: {error.Message}");
                ConnectionError = Localization.Get("MacFallbackUnavailable");
                lock (_gate) { _lastState = null; _playing = false; _lastElapsedMicros = _lastTimestampMicros = 0; }
                EmitStateChanged(null!);
            }
            try { await Task.Delay(3000, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal void ProcessJsonLine(string line)
    {
        PlayerState? state;
        lock (_gate) { ParseJsonLine(line); state = _lastState?.DeepCopy(); }
        EmitStateChanged(state!);
    }

    private void ParseJsonLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);

            if (!doc.RootElement.TryGetProperty("payload", out var payload))
                return;

            // empty → no active media
            if (payload.ValueKind == JsonValueKind.Null || payload.ValueKind == JsonValueKind.Object &&
                !payload.EnumerateObject().Any())
            {
                _lastState = null;
                _playing = false;
                _lastElapsedMicros = _lastTimestampMicros = 0;
                return;
            }

            // metadata fields
            string title = payload.TryGetProperty("title", out var jTitle)
                ? jTitle.GetString() ?? ""
                : _lastState?.Title ?? "";

            string artist = payload.TryGetProperty("artist", out var jArtist)
                ? jArtist.GetString() ?? ""
                : _lastState?.Artists.FirstOrDefault() ?? "";

            string album = payload.TryGetProperty("album", out var jAlbum)
                ? jAlbum.GetString() ?? ""
                : _lastState?.Album ?? "";

            long durationMicros = payload.TryGetProperty("durationMicros", out var jDur)
                ? jDur.GetInt64()
                : (long)(_lastState?.Duration.TotalMicroseconds ?? 0);

            // playback tick baseline
            long elapsedMicros = payload.TryGetProperty("elapsedTimeMicros", out var jEl)
                ? jEl.GetInt64()
                : _lastElapsedMicros;

            long timestampMicros = payload.TryGetProperty("timestampEpochMicros", out var jTs)
                ? jTs.GetInt64()
                : _lastTimestampMicros;

            bool playing = payload.TryGetProperty("playing", out var jPlay)
                ? jPlay.GetBoolean()
                : _playing;

            string bundle = payload.TryGetProperty("bundleIdentifier", out var xBid)
                ? xBid.GetString() ?? ""
                : _lastState?.SourceApp ?? "";

            // update baseline for timer
            _lastTimestampMicros = timestampMicros;
            _lastElapsedMicros = elapsedMicros;
            _playing = playing;
            _lastTickMicros = NowMicros();

            // compute current position at packet arrival
            long diff = timestampMicros > 0 ? Math.Max(0, NowMicros() - timestampMicros) : 0;
            long positionMicros = Math.Max(0, playing ? elapsedMicros + diff : elapsedMicros);
            if (durationMicros > 0) positionMicros = Math.Min(positionMicros, durationMicros);

            var newState = new PlayerState
            {
                Title = title,
                Album = album,
                SourceApp = bundle,
                Duration = TimeSpan.FromMicroseconds(durationMicros),
                Position = TimeSpan.FromMicroseconds(positionMicros),
                Playing = playing
            };
            newState.Artists.Add(artist);

            _lastState = newState;

        }
        catch
        {
            // ignore broken JSON lines
        }
    }

    // --------------------------------------------------------------
    // Timer: incremental position update every 200ms
    // --------------------------------------------------------------
    private void StartPositionTimer()
    {
        _posTimer = new Timer(200);
        _posTimer.AutoReset = true;

        _posTimer.Elapsed += (_, _) =>
        {
            PlayerState updated;
            lock (_gate)
            {
                if (_disposed || _lastState is not { Playing: true } state) return;
                long now = NowMicros();
                long delta = Math.Max(0, now - _lastTickMicros);
                _lastTickMicros = now;
                long newPos = (long)state.Position.TotalMicroseconds + delta;
                if (state.Duration > TimeSpan.Zero) newPos = Math.Min(newPos, (long)state.Duration.TotalMicroseconds);
                updated = state.DeepCopy();
                updated.Position = TimeSpan.FromMicroseconds(newPos);
                _lastState = updated;
            }
            EmitStateChanged(updated);
        };

        _posTimer.Start();
    }

    private static long NowMicros()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;

    // --------------------------------------------------------------
    // Control commands
    // https://github.com/ungive/media-control/blob/master/bin/media-control
    // --------------------------------------------------------------
    public override Task PlayAsync() => RunCmd("play");
    public override Task PauseAsync() => RunCmd("pause");
    public override Task TogglePlayPauseAsync() => RunCmd("toggle-play-pause");
    public override Task NextAsync() => RunCmd("next-track");
    public override Task PreviousAsync() => RunCmd("previous-track");
    public override Task SeekAsync(TimeSpan p) => RunCmd("seek", Math.Max(0, p.TotalSeconds).ToString(CultureInfo.InvariantCulture));

    private Task RunCmd(params string[] arguments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return MacProcess.RunAsync(MacProcess.StartInfo(_executable, arguments), _cts?.Token ?? default);
    }
}
