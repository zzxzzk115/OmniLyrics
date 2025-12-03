using OmniLyrics.Core;
using OmniLyrics.Core.Cli;
using OmniLyrics.Core.Lyrics;
using OmniLyrics.Web;

public static class LyricsCliRunner
{
    public static async Task RunAsync(IPlayerBackend backend, string[] args)
    {
        var opt = CliParser.Parse(args);

        // Control mode -> send UDP command (do NOT initialize backends)
        if (opt.Control != ControlAction.None)
        {
            await ControlSender.SendAsync(opt.ToCommandString());
            return;
        }

        // Normal Lyrics mode -> start backend + command server
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        // Create the CLI instance
        var cli = CliFactory.Create(opt.Mode, backend);

        // Start backend normally
        await backend.StartAsync(cts.Token);

        // Start UDP control server
        var server = new CommandServer(backend);
        _ = server.StartAsync(cts.Token);

        // Start WebAPI server
        var lyrics = new LyricsService();
        var webServer = new WebApiServer(backend, cli);
        _ = webServer.StartAsync(cts.Token);

        // Start lyrics UI
        await cli.RunAsync(cts.Token);
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