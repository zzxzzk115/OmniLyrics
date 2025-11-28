using System.Diagnostics;
using System.Text.Json;
using NMSL.Core;

namespace NMSL.Backends.Mac;

public class MacOSMediaControlBackend : IPlayerBackend
{
    private PlayerState? _lastState;
    private Process? _proc;
    private Task? _readLoop;

    public event EventHandler<PlayerState>? OnStateChanged;

    private long _lastTimestampMicros = 0;
    private long _lastElapsedMicros = 0;
    private bool _playing = false;

    public PlayerState? GetCurrentState() => _lastState;

    public Task StartAsync(CancellationToken token)
    {
        StartStreamLoop(token);
        return Task.CompletedTask;
    }

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

        _readLoop = Task.Run(async () =>
        {
            using var reader = _proc.StandardOutput;

            while (!reader.EndOfStream && !token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

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

            // Empty diff → nothing playing
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.EnumerateObject().Count() == 0)
            {
                _lastState = null;
                OnStateChanged?.Invoke(this, null!);
                return;
            }

            // Extract values
            string title = payload.TryGetProperty("title", out var xTitle)
                ? xTitle.GetString() ?? ""
                : _lastState?.Title ?? "";

            string artist = payload.TryGetProperty("artist", out var xArtist)
                ? xArtist.GetString() ?? ""
                : _lastState?.Artists.FirstOrDefault() ?? "";

            long duration = payload.TryGetProperty("durationMicros", out var xDur)
                ? xDur.GetInt64()
                : (_lastState?.Duration.Ticks * 10) ?? 0;

            long elapsed = payload.TryGetProperty("elapsedTimeMicros", out var xEl)
                ? xEl.GetInt64()
                : _lastElapsedMicros;

            long ts = payload.TryGetProperty("timestampEpochMicros", out var xTs)
                ? xTs.GetInt64()
                : _lastTimestampMicros;

            bool playing = payload.TryGetProperty("playing", out var xPlay)
                ? xPlay.GetBoolean()
                : _playing;

            // store for time calculation
            _lastTimestampMicros = ts;
            _lastElapsedMicros = elapsed;
            _playing = playing;

            // compute real position
            long nowMicros = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
            long diffMicros = nowMicros - ts;

            long position = playing
                ? elapsed + diffMicros
                : elapsed;

            var newState = new PlayerState
            {
                Title = title,
                SourceApp = "media-control",
                Duration = TimeSpan.FromMicroseconds(duration),
                Position = TimeSpan.FromMicroseconds(position),
                Playing = playing
            };

            newState.Artists.Add(artist);

            _lastState = newState;
            OnStateChanged?.Invoke(this, newState);
        }
        catch
        {
            // ignore broken JSON lines
        }
    }

    // macOS media-control supports these commands:
    public Task PlayAsync() => RunCmd("play");
    public Task PauseAsync() => RunCmd("pause");
    public Task TogglePlayPauseAsync() => RunCmd("togglePlayPause");
    public Task NextAsync() => RunCmd("next");
    public Task PreviousAsync() => RunCmd("previous");

    public Task SeekAsync(TimeSpan position)
        => RunCmd($"seek {position.TotalSeconds}");

    private Task RunCmd(string arg)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "media-control",
            Arguments = arg,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(psi);
        return Task.CompletedTask;
    }
}