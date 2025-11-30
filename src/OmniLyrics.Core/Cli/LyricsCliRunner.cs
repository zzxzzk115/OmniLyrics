using OmniLyrics.Core;
using OmniLyrics.Core.Cli;

public static class LyricsCliRunner
{
    public static async Task RunAsync(IPlayerBackend backend, string[] args)
    {
        var opt = CliParser.Parse(args);

        if (opt.Control != ControlAction.None)
        {
            var cts = new CancellationTokenSource();
            await backend.StartAsync(cts.Token);

            await WaitBackendReadyAsync(backend);

            await ExecuteControlCommand(backend, opt);
            return;
        }

        BaseLyricsCli cli = CliFactory.Create(opt.Mode, backend);

        var loopCts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            loopCts.Cancel();
        };

        await backend.StartAsync(loopCts.Token);
        await cli.RunAsync(loopCts.Token);
    }

    private static async Task WaitBackendReadyAsync(IPlayerBackend backend)
    {
        for (int i = 0; i < 20; i++)
        {
            var state = backend.GetCurrentState();
            if (state != null)
                return;

            await Task.Delay(100);
        }
    }

    private static async Task ExecuteControlCommand(IPlayerBackend backend, CliOptions opt)
    {
        switch (opt.Control)
        {
            case ControlAction.Play:
                await backend.PlayAsync();
                break;

            case ControlAction.Pause:
                await backend.PauseAsync();
                break;

            case ControlAction.Toggle:
                await backend.TogglePlayPauseAsync();
                break;

            case ControlAction.Next:
                await backend.NextAsync();
                break;

            case ControlAction.Prev:
                await backend.PreviousAsync();
                break;

            case ControlAction.Seek:
                if (opt.SeekPositionSeconds.HasValue)
                    await backend.SeekAsync(TimeSpan.FromSeconds(opt.SeekPositionSeconds.Value));
                break;
        }
    }
}