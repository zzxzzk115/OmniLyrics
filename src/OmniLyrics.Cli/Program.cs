using OmniLyrics.Core;
using OmniLyrics.Backends.Dynamic;
using OmniLyrics.Cli;

if (await ConfigurationCommand.TryRunAsync(args)) return;
try
{
    var options = OmniLyrics.Core.Cli.CliParser.Parse(args);
    if (options.Control == ControlAction.None)
        await OmniLyrics.Backends.Mac.MacEnvironment.CheckConsoleStartupAsync(
            !Console.IsInputRedirected && !Console.IsOutputRedirected && options.Mode is not ("line" or "json"));
    await LyricsCliRunner.RunAsync(() => new DynamicBackend(), args);
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    Environment.ExitCode = 2;
}
catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException)
{
    Console.Error.WriteLine(Localization.Text("Could not start the control service. Check the configured address and whether its ports are already in use."));
    Environment.ExitCode = 2;
}
