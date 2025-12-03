namespace OmniLyrics.Core.Cli
{
    public static class CliParser
    {
        public static CliOptions Parse(string[] args)
        {
            var opts = new CliOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                // mode
                if (arg == "--mode" || arg == "-m")
                {
                    if (i + 1 < args.Length)
                        opts.Mode = args[++i].ToLowerInvariant();
                    continue;
                }
                if (arg.StartsWith("--mode="))
                {
                    opts.Mode = arg.Substring("--mode=".Length).ToLowerInvariant();
                    continue;
                }

                // control
                if (arg == "--control" || arg == "-c")
                {
                    if (i + 1 < args.Length)
                        opts.Control = ParseControl(args[++i]);
                    continue;
                }
                if (arg.StartsWith("--control="))
                {
                    opts.Control = ParseControl(arg.Substring("--control=".Length));
                    continue;
                }

                // seek seconds
                if (opts.Control == ControlAction.Seek)
                {
                    if (double.TryParse(arg, out double sec))
                        opts.SeekPositionSeconds = sec;
                }
            }

            return opts;
        }

        private static ControlAction ParseControl(string text)
        {
            return text.ToLowerInvariant() switch
            {
                "play" => ControlAction.Play,
                "pause" => ControlAction.Pause,
                "toggle" => ControlAction.Toggle,
                "prev" => ControlAction.Prev,
                "next" => ControlAction.Next,
                "seek" => ControlAction.Seek,
                _ => ControlAction.None
            };
        }
    }
}

public enum ControlAction
{
    None,
    Play,
    Pause,
    Toggle,
    Prev,
    Next,
    Seek
}

public sealed class CliOptions
{
    public string Mode { get; set; } = "default";

    public ControlAction Control { get; set; } = ControlAction.None;

    public double? SeekPositionSeconds { get; set; }
}