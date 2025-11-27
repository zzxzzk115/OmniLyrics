namespace NMSL.Core.Cli;

public static class CliFactory
{
    public static BaseLyricsCli Create(string mode, IPlayerBackend backend)
    {
        // normalize
        mode = mode?.Trim().ToLowerInvariant() ?? "default";

        return mode switch
        {
            "line" => new LineLyricsCli(backend),
            "full" => new DefaultLyricsCli(backend),
            _ => new DefaultLyricsCli(backend),
        };
    }
}