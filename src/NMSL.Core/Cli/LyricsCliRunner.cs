namespace NMSL.Core.Cli;

public static class LyricsCliRunner
{
    public static async Task RunAsync(IPlayerBackend backend, string[] args)
    {
        string mode = CliModeParser.Parse(args);

        BaseLyricsCli cli = CliFactory.Create(mode, backend);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await cli.RunAsync(cts.Token);
    }
}