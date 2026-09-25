using OmniLyrics.Core;
using OmniLyrics.Core.Cli;
using OmniLyrics.Core.Configuration;
using System.Runtime.InteropServices;

public static class LyricsCliRunner
{
    public static async Task RunAsync(Func<IPlayerBackend> createBackend, string[] args)
    {
        var opt = CliParser.Parse(args);
        var saved = UserConfiguration.LoadServer();
        var serverSettings = saved with {
            ListenAddress = opt.ListenAddress ?? saved.ListenAddress,
            ControlHost = opt.ControlHost ?? saved.ControlHost,
            HttpPort = opt.HttpPort ?? saved.HttpPort,
            UdpPort = opt.UdpPort ?? saved.UdpPort
        };
        UserConfiguration.ValidateServer(serverSettings);

        // Control mode -> send UDP command (do NOT initialize backends)
        if (opt.Control != ControlAction.None)
        {
            await ControlSender.SendAsync(opt.ToCommandString(), serverSettings.ControlHost, serverSettings.UdpPort);
            return;
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += cancel;
        using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM,
            signal => { signal.Cancel = true; cts.Cancel(); });
        // The terminal view is TUI; line/JSON streams are CLI consumers.
        var role = opt.Mode is "line" or "json" ? ServiceRole.Cli : ServiceRole.Tui;
        await using var session = new SharedPlayerSession(createBackend, role, () => serverSettings);
        var cli = CliFactory.Create(opt.Mode, session);
        try { await cli.RunAsync(cts.Token); }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally { Console.CancelKeyPress -= cancel; }

    }
}

public static class ControlExtensions
{
    public static string ToCommandString(this CliOptions opt)
    {
        return opt.Control switch
        {
            ControlAction.Play => "play",
            ControlAction.Pause => "pause",
            ControlAction.Toggle => "toggle",
            ControlAction.Next => "next",
            ControlAction.Prev => "prev",
            ControlAction.Seek =>
                opt.SeekPositionSeconds.HasValue
                    ? $"seek {opt.SeekPositionSeconds.Value}"
                    : "seek 0",
            _ => ""
        };
    }
}
