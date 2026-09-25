using OmniLyrics.Core;
using OmniLyrics.Backends.CiderV3;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Lyrics;

namespace OmniLyrics.Cli;

internal static class ConfigurationCommand
{
    public static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.FirstOrDefault() != "config") return false;
        try
        {
            if (args.Length == 1)
            {
                if (Console.IsInputRedirected)
                    throw new InvalidOperationException("Use config show, config lyrics prefetch on|off or config cider for connection settings.");
                Console.WriteLine(Localization.Get("ConfigMenu"));
                Console.Write(Localization.Get("ChoosePreferences"));
                var choice = Console.ReadLine();
                if (choice == "2") return await TryRunAsync(["config", "cider"]);
                if (choice == "3")
                {
                    Console.Write(Localization.Get("LanguagePrompt"));
                    var language = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(language)) return await TryRunAsync(["config", "language", language]);
                    return true;
                }
                if (choice != "1") return true;
                var lyrics = UserConfiguration.LoadLyrics();
                Console.WriteLine(Localization.Text("Word-timed karaoke lyrics have highest priority for every player."));
                Console.Write(Localization.Text("Prefetch upcoming tracks where available? [Y/n]: "));
                var enabled = !string.Equals(Console.ReadLine()?.Trim(), "n", StringComparison.OrdinalIgnoreCase);
                string ReadOption(string prompt, string current)
                {
                    Console.Write(Localization.Get(prompt));
                    var value = Console.ReadLine()?.Trim();
                    return string.IsNullOrEmpty(value) ? current : value;
                }
                lyrics = lyrics with
                {
                    Prefetch = enabled,
                    SearchStrategy = ReadOption("LyricStrategyPrompt", lyrics.SearchStrategy),
                    MatchMode = ReadOption("LyricMatchPrompt", lyrics.MatchMode)
                };
                lyrics = SetSources(lyrics, ReadOption("LyricSourcesPrompt", string.Join(',', LyricsSearchPreferences.Sources(lyrics))));
                lyrics = lyrics with { PreferredSource = ReadOption("LyricPriorityPrompt", lyrics.PreferredSource) };
                UserConfiguration.SaveLyrics(lyrics);
                Console.WriteLine(Localization.Text("Saved shared lyric preferences."));
            }
            else if (args.SequenceEqual(new[] { "config", "cider" }))
            {
                if (Console.IsInputRedirected)
                    throw new InvalidOperationException("Use config cider token --stdin or config cider none for non-interactive setup.");
                Console.WriteLine(Localization.Text("Cider connection\n  1. Use API token\n  2. No token (Require API Tokens is disabled in Cider)\n  Enter. Cancel"));
                Console.Write(Localization.Text("Choose [1/2]: "));
                var choice = Console.ReadLine();
                if (choice is not ("1" or "2")) return true;
                Save(choice == "1" ? "token" : "none", redirected: false);
            }
            else if (args.SequenceEqual(new[] { "config", "show" }))
            {
                Console.WriteLine(Localization.Format("ConfigLocation", UserConfiguration.SettingsPath));
                Console.WriteLine(Localization.Format("LanguageSummary", UserConfiguration.LoadLanguage()));
                var lyrics = UserConfiguration.LoadLyrics();
                Console.WriteLine(Localization.Format("LyricSummary", lyrics.Prefetch ? "on" : "off", lyrics.PrefetchCount));
                Console.WriteLine(Localization.Format("LyricPolicySummary", lyrics.SearchStrategy, lyrics.MatchMode,
                    string.Join(", ", LyricsSearchPreferences.Sources(lyrics)), lyrics.PreferredSource));
                Console.WriteLine(Localization.Format("AuthSummary", UserConfiguration.EffectiveAuthentication()));
                Console.WriteLine(Localization.Format("IntegrationSummary", UserConfiguration.LoadCider().Integration));
                Console.WriteLine(Localization.Format("TokenSummary", string.IsNullOrEmpty(UserConfiguration.ResolveCiderToken()) ? Localization.Get("TokenUnused") : Localization.Get("TokenHidden")));
                var server = UserConfiguration.LoadServer();
                Console.WriteLine(Localization.Format("ServerSummary", server.ListenAddress, server.HttpPort, server.UdpPort, server.ControlHost));
                if (UserConfiguration.HasEnvironmentOverride) Console.WriteLine(Localization.Text("Cider environment overrides are present."));
            }
            else if (args.Length == 3 && args[1] == "language")
            {
                UserConfiguration.SaveLanguage(args[2]);
                Localization.Reload();
                Console.WriteLine(Localization.Get("LanguageSaved"));
            }
            else if (args.SequenceEqual(new[] { "config", "test" }) || args.SequenceEqual(new[] { "config", "cider", "test" }))
            {
                using var api = CiderV3Api.CreateDefault();
                var connected = await api.TryGetActiveAsync();
                Console.WriteLine(connected ? Localization.Text("Cider API connected.") : Localization.Text("Cannot connect. Check Cider, its authentication setting, and your token."));
                if (!connected) Environment.ExitCode = 1;
            }
            else if (args.Length == 4 && args[1] == "cider" && args[2] == "integration")
            {
                UserConfiguration.SaveCider(UserConfiguration.LoadCider().Authentication, integration: args[3]);
                Console.WriteLine(Localization.Text("Saved Cider playback integration."));
            }
            else if (args.Length == 4 && args[1] == "lyrics")
            {
                var lyrics = UserConfiguration.LoadLyrics();
                if (args[2] == "prefetch" && args[3] is "on" or "off")
                    lyrics = lyrics with { Prefetch = args[3] == "on" };
                else if (args[2] == "count" && int.TryParse(args[3], out var count))
                    lyrics = lyrics with { PrefetchCount = count };
                else if (args[2] == "strategy") lyrics = lyrics with { SearchStrategy = args[3] };
                else if (args[2] == "match") lyrics = lyrics with { MatchMode = args[3] };
                else if (args[2] == "sources") lyrics = SetSources(lyrics, args[3]);
                else if (args[2] == "prefer") lyrics = lyrics with { PreferredSource = args[3] };
                else throw new InvalidOperationException(Localization.Get("LyricsUsage"));
                UserConfiguration.SaveLyrics(lyrics);
                Console.WriteLine(Localization.Text("Saved shared lyric preferences."));
            }
            else if (args.Length >= 2 && args[1] == "server")
            {
                var server = UserConfiguration.LoadServer();
                if (args.Length == 3 && args[2] is "local" or "lan")
                    server = server with { ListenAddress = args[2] == "lan" ? "0.0.0.0" : "127.0.0.1" };
                else if (args.Length == 4 && args[2] == "listen")
                    server = server with { ListenAddress = args[3] };
                else if (args.Length == 4 && args[2] == "target")
                    server = server with { ControlHost = args[3] };
                else if (args.Length == 5 && args[2] == "ports" && int.TryParse(args[3], out var http) && int.TryParse(args[4], out var udp))
                    server = server with { HttpPort = http, UdpPort = udp };
                else throw new InvalidOperationException("Usage: config server local|lan|listen ADDRESS|target HOST|ports HTTP UDP");
                UserConfiguration.SaveServer(server);
                Console.WriteLine(Localization.Text("Saved server settings. Restart a running control server to apply listening changes."));
            }
            else if (args.Length is 3 or 4 && args[1] == "cider" && args[2] is "token" or "none"
                     && (args.Length == 3 || args[2] == "token" && args[3] == "--stdin"))
                Save(args[2], args.Length == 4);
            else
                throw new InvalidOperationException(Localization.Get("ConfigUsage"));
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(Localization.Text("Configuration cancelled."));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            // Do not echo malformed configuration or a supplied secret.
            Console.Error.WriteLine(error is InvalidOperationException or ArgumentException ? Localization.Text(error.Message)
                : Localization.Text("Unable to read or save configuration. Check the configuration file and permissions."));
            Environment.ExitCode = 1;
        }
        return true;
    }

    private static LyricsSettings SetSources(LyricsSettings settings, string value)
    {
        var sources = value.Split(',', StringSplitOptions.TrimEntries);
        if (sources.Any(source => source is not ("player" or "qq" or "netease")))
            throw new ArgumentException(Localization.Get("LyricsUsage"));
        return settings with { PlayerSource = sources.Contains("player"), QQMusicSource = sources.Contains("qq"), NeteaseSource = sources.Contains("netease") };
    }

    private static void Save(string mode, bool redirected)
    {
        string? token = null;
        if (mode == "token")
        {
            if (redirected) token = Console.In.ReadToEnd().Trim();
            else
            {
                if (Console.IsInputRedirected)
                    throw new InvalidOperationException(Localization.Text("Use --stdin for piped token input. Tokens are not accepted as command arguments."));
                Console.Write(Localization.Text("API token (hidden; blank keeps the saved token): "));
                var buffer = new List<char>();
                while (true)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Enter) break;
                    if (key.Key == ConsoleKey.Escape || key.KeyChar == '\u0003')
                        throw new OperationCanceledException();
                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (buffer.Count > 0) buffer.RemoveAt(buffer.Count - 1);
                    }
                    else if (!char.IsControl(key.KeyChar)) buffer.Add(key.KeyChar);
                }
                Console.WriteLine();
                token = new string(buffer.ToArray()).Trim();
            }
            if (string.IsNullOrEmpty(token) && string.IsNullOrEmpty(UserConfiguration.ReadSavedToken()))
                throw new InvalidOperationException(Localization.Text("No token entered and no saved token exists. Nothing was changed."));
        }
        UserConfiguration.SaveCider(mode, token);
        Console.WriteLine(Localization.Format("AuthSaved", mode));
        if (UserConfiguration.HasEnvironmentOverride) Console.WriteLine(Localization.Text("Cider environment overrides are present; remove them to use only saved settings."));
    }
}
