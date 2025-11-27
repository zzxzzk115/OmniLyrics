namespace NMSL.Core.Cli;

public static class CliModeParser
{
    public static string Parse(string[] args)
    {
        string mode = "default";

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--mode" || arg == "-m")
            {
                if (i + 1 < args.Length)
                    mode = args[i + 1].ToLowerInvariant();
            }
            else if (arg.StartsWith("--mode="))
            {
                mode = arg.Substring("--mode=".Length).ToLowerInvariant();
            }
        }

        return mode;
    }
}