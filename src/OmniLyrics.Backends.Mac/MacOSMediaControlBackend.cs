using System.Diagnostics;
using System.Text.Json;
using OmniLyrics.Core;
using Timer = System.Timers.Timer;

namespace OmniLyrics.Backends.Mac;

public class MacOSMediaControlBackend : BasePlayerBackend, IDisposable
{
    private long _lastElapsedMicros;
    private PlayerState? _lastState;
    private long _lastTickMicros;

    // timestamp / elapsed provided by stream
    private long _lastTimestampMicros;
    private bool _playing;

    // timer for incremental position updates
    private Timer? _posTimer;
    private Process? _proc;
    private Task? _streamLoop;

    public void Dispose()
    {
        _proc?.Dispose();
        _streamLoop?.Dispose();
        _posTimer?.Dispose();
    }

    public override PlayerState? GetCurrentState() => _lastState;

    public override Task StartAsync(CancellationToken token)
    {
        StartStreamLoop(token);
        StartPositionTimer();
        return Task.CompletedTask;
    }

    // --------------------------------------------------------------
    // Stream loop: metadata updates (title/artist/duration/playing)
    // --------------------------------------------------------------
    private void StartStreamLoop(CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "media-control",
            Arguments = "stream --micros",
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _proc = Process.Start(psi);

        if (_proc == null)
            throw new Exception("Failed to start media-control");

        _streamLoop = Task.Run(async () =>
        {
            using var reader = _proc.StandardOutput;

            while (!reader.EndOfStream && !token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                    ProcessJsonLine(line);
            }
        }, token);
    }

    private void ProcessJsonLine(string line)
    {
        try
        {
            var doc = JsonDocument.Parse(line);

            if (!doc.RootElement.TryGetProperty("payload", out var payload))
                return;

            // empty → no active media
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.EnumerateObject().Count() == 0)
            {
                _lastState = null;
                EmitStateChanged(null!);
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
            long diff = NowMicros() - timestampMicros;
            long positionMicros = playing ? elapsedMicros + diff : elapsedMicros;

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
            EmitStateChanged(newState);
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
            var state = _lastState;
            if (state == null || !state.Playing)
                return;

            long now = NowMicros();
            long delta = now - _lastTickMicros;
            _lastTickMicros = now;

            long newPos = (long)state.Position.TotalMicroseconds + delta;

            // clamp to duration
            if (state.Duration > TimeSpan.Zero &&
                newPos > (long)state.Duration.TotalMicroseconds)
            {
                newPos = (long)state.Duration.TotalMicroseconds;
            }

            var updated = state.DeepCopy();
            updated.Position = TimeSpan.FromMicroseconds(newPos);

            _lastState = updated;
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
    public override Task SeekAsync(TimeSpan p) => RunCmd($"seek {p.TotalSeconds}");

    private Task RunCmd(string arg)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "media-control",
            Arguments = arg,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        return Task.CompletedTask;
    }
}