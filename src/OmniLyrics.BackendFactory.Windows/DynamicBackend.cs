using OmniLyrics.Core;
using OmniLyrics.Backends.CiderV3;
using OmniLyrics.Backends.Windows;

namespace OmniLyrics.Backends.Dynamic;

public class DynamicBackend : BasePlayerBackend, IDisposable
{
    private IPlayerBackend? _current;
    private CancellationTokenSource _cts = new();
    private Task? _monitorLoop;
    private CiderV3Api _ciderApiV3 = CiderV3Api.CreateDefault();

    public override PlayerState? GetCurrentState() => _current?.GetCurrentState();

    public override async Task StartAsync(CancellationToken token)
    {
        _monitorLoop = Task.Run(() => MonitorLoopAsync(token), token);
        await SwitchBackendIfNeededAsync();
    }

    private async Task MonitorLoopAsync(CancellationToken globalToken)
    {
        while (!globalToken.IsCancellationRequested)
        {
            await SwitchBackendIfNeededAsync();
            await Task.Delay(1000, globalToken);
        }
    }

    private async Task SwitchBackendIfNeededAsync()
    {
        var (backendName, backend) = await DetectBackendAsync();

        if (_current != null && _current.GetType() == backend.GetType())
            return;

        // Switch
        Console.WriteLine($"[DynamicBackend] Switching backend → {backendName}");

        _cts.Cancel();
        _cts = new();

        _current = backend;
        _current.OnStateChanged += (_, state) => EmitStateChanged(state);
        await _current.StartAsync(_cts.Token);
    }

    private async Task<(string, IPlayerBackend)> DetectBackendAsync()
    {
        // API-priority
        var isPlaying = await _ciderApiV3.TryGetIsPlayingAsync();
        if (isPlaying)
            return ("CiderV3", new CiderV3Backend());

        // OS fallback
        if (OperatingSystem.IsWindows())
            return ("SMTC", new SMTCBackend());

        throw new PlatformNotSupportedException();
    }

    // Commands routed to current backend
    public override Task PlayAsync() => _current?.PlayAsync() ?? Task.CompletedTask;
    public override Task PauseAsync() => _current?.PauseAsync() ?? Task.CompletedTask;
    public override Task TogglePlayPauseAsync() => _current?.TogglePlayPauseAsync() ?? Task.CompletedTask;
    public override Task NextAsync() => _current?.NextAsync() ?? Task.CompletedTask;
    public override Task PreviousAsync() => _current?.PreviousAsync() ?? Task.CompletedTask;
    public override Task SeekAsync(TimeSpan pos) => _current?.SeekAsync(pos) ?? Task.CompletedTask;

    public void Dispose()
    {
        _cts.Cancel();
    }
}