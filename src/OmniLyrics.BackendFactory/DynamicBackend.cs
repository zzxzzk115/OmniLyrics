using OmniLyrics.Core;
using OmniLyrics.Backends.CiderV3;
using OmniLyrics.Backends.Linux;
using OmniLyrics.Backends.Mac;

namespace OmniLyrics.Backends.Dynamic;

public class DynamicBackend : BasePlayerBackend, IDisposable
{
    private readonly Dictionary<string, IPlayerBackend> _backends;
    private IPlayerBackend? _current;

    private CancellationTokenSource _cts = new();
    private Task? _monitorLoop;

    private readonly CiderV3Api _ciderApiV3 = CiderV3Api.CreateDefault();

    public DynamicBackend()
    {
        _backends = new Dictionary<string, IPlayerBackend>();

        if (OperatingSystem.IsLinux())
            _backends["MPRIS"] = new MPRISBackend();

        if (OperatingSystem.IsMacOS())
            _backends["MediaControl"] = new MacOSMediaControlBackend();

        _backends["CiderV3"] = new CiderV3Backend();

        foreach (var kv in _backends)
            kv.Value.OnStateChanged += HandleSubBackendStateChanged;
    }

    public override PlayerState? GetCurrentState() => _current?.GetCurrentState();

    public override async Task StartAsync(CancellationToken token)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        foreach (var backend in _backends.Values)
            _ = backend.StartAsync(_cts.Token);

        await Task.CompletedTask;
    }

    private void HandleSubBackendStateChanged(object? sender, PlayerState state)
    {
        if (sender is not IPlayerBackend b)
            return;

        if (state == null)
            return;

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

    public void Dispose()
    {
        _cts.Cancel();
    }
}