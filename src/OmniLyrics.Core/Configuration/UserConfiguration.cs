using System.Text.Json.Nodes;
using System.Text.Json;
using System.Net;

namespace OmniLyrics.Core.Configuration;

public sealed record CiderConnectionSettings(string Authentication, string Integration);
public sealed record ServerSettings(string ListenAddress, int HttpPort, int UdpPort, string ControlHost);
public sealed record LyricsSettings(bool Prefetch, int PrefetchCount,
    string SearchStrategy = "fallback", string MatchMode = "balanced",
    bool PlayerSource = true, bool QQMusicSource = true, bool NeteaseSource = true,
    string PreferredSource = "qq");
public sealed record AppearanceSettings(string Preset, bool Locked, bool ShowLogo, bool ShowPlayerInfo,
    bool ApproximateHighlight = true, bool ShowTranslation = true, double FontSize = 32, double TranslationFontSize = 18,
    string TextColor = "#BAC0CC", string HighlightColor = "#F3C879", string BackgroundColor = "#161A23", double BackgroundOpacity = .6,
    bool UseBlur = true, string ThemeMode = "dark", string AccentColor = "#FA586A", double? UiScale = null);

/// <summary>Shared by all frontends. Credentials are separate from ordinary settings.</summary>
public static partial class UserConfiguration
{
    public static string DirectoryPath
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("OMNILYRICS_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniLyrics");
            if (OperatingSystem.IsMacOS())
                return Path.Combine(home, "Library", "Application Support", "OmniLyrics");
            // A bundled desktop shell may override XDG_CONFIG_HOME for its own
            // config. Use the same user location for CLI, GUI and shell helpers.
            return Path.Combine(home, ".config", "omnilyrics");
        }
    }

    public static string SettingsPath => Path.Combine(DirectoryPath, "config.json");
    public static string TokenPath => Path.Combine(DirectoryPath, "cider-token");

    public static string LoadLanguage() => ReadDocument()["language"]?.GetValue<string>() ?? "auto";

    public static AppearanceSettings LoadAppearance() => ParseAppearance(ReadDocument());

    private static AppearanceSettings ParseAppearance(JsonObject root)
    {
        var appearance = root["appearance"];
        var preset = appearance?["preset"]?.GetValue<string>() ?? "classic";
        var settings = new AppearanceSettings(preset, appearance?["locked"]?.GetValue<bool>() ?? false,
            appearance?["showLogo"]?.GetValue<bool>() ?? true,
            appearance?["showPlayerInfo"]?.GetValue<bool>() ?? true,
            appearance?["approximateHighlight"]?.GetValue<bool>() ?? true,
            appearance?["showTranslation"]?.GetValue<bool>() ?? true,
            appearance?["fontSize"]?.GetValue<double>() ?? 32,
            appearance?["translationFontSize"]?.GetValue<double>() ?? 18,
            appearance?["textColor"]?.GetValue<string>() ?? "#BAC0CC",
            appearance?["highlightColor"]?.GetValue<string>() ?? "#F3C879",
            appearance?["backgroundColor"]?.GetValue<string>() ?? "#161A23",
            appearance?["backgroundOpacity"]?.GetValue<double>() ?? .6,
            appearance?["useBlur"]?.GetValue<bool>() ?? true,
            appearance?["themeMode"]?.GetValue<string>() ?? "dark",
            appearance?["accentColor"]?.GetValue<string>() ?? "#FA586A",
            appearance?["uiScale"]?.GetValue<double>());
        ValidateAppearance(settings);
        return settings;
    }

    public static void SaveAppearance(AppearanceSettings settings)
    {
        ValidateAppearance(settings);
        var root = ReadDocument();
        root["version"] = 1;
        if (root["appearance"] is not JsonObject) root["appearance"] = new JsonObject();
        var appearance = root["appearance"]!;
        appearance["preset"] = settings.Preset;
        appearance["locked"] = settings.Locked;
        appearance["showLogo"] = settings.ShowLogo;
        appearance["showPlayerInfo"] = settings.ShowPlayerInfo;
        appearance["approximateHighlight"] = settings.ApproximateHighlight;
        appearance["showTranslation"] = settings.ShowTranslation;
        appearance["fontSize"] = settings.FontSize;
        appearance["translationFontSize"] = settings.TranslationFontSize;
        appearance["textColor"] = settings.TextColor;
        appearance["highlightColor"] = settings.HighlightColor;
        appearance["backgroundColor"] = settings.BackgroundColor;
        appearance["backgroundOpacity"] = settings.BackgroundOpacity;
        appearance["useBlur"] = settings.UseBlur;
        appearance["themeMode"] = settings.ThemeMode;
        appearance["accentColor"] = settings.AccentColor;
        appearance["uiScale"] = settings.UiScale;
        WritePrivate(SettingsPath, root.ToJsonString(new() { WriteIndented = true }) + "\n");
    }

    private static void ValidateAppearance(AppearanceSettings settings)
    {
        if (settings.Preset is not ("classic" or "compact" or "focus" or "portrait" or "fullscreen")) throw new ArgumentException("Unknown appearance preset.");
        if (!double.IsFinite(settings.FontSize) || settings.FontSize is < 16 or > 72
            || !double.IsFinite(settings.TranslationFontSize) || settings.TranslationFontSize is < 12 or > 40
            || !double.IsFinite(settings.BackgroundOpacity) || settings.BackgroundOpacity is < 0 or > 1)
            throw new ArgumentException("Invalid appearance size or opacity.");
        if (settings.UiScale is { } scale && (!double.IsFinite(scale) || scale is < .75 or > 3))
            throw new ArgumentException("UI scale must be null (system) or between 0.75 and 3.");
        ThemePalette.FromAppearance(settings).Validate();
    }

    public static IReadOnlyList<NamedThemePreset> LoadThemePresets() => ParseThemePresets(ReadDocument());

    private static List<NamedThemePreset> ParseThemePresets(JsonObject root)
    {
        if (root["themePresets"] is null) return [];
        if (root["themePresets"] is not JsonArray array || array.Count > 32)
            throw new InvalidDataException("Theme presets must be an array with at most 32 entries.");
        var presets = new List<NamedThemePreset>();
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var node in array)
        {
            var preset = node?.Deserialize<NamedThemePreset>(options)
                ?? throw new InvalidDataException("Invalid theme preset.");
            preset.Validate();
            if (presets.Any(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Theme preset names must be unique.");
            presets.Add(preset);
        }
        return presets;
    }

    public static void SaveThemePreset(NamedThemePreset preset)
    {
        preset = preset with { Name = preset.Name.Trim() };
        preset.Validate();
        var root = ReadDocument();
        var presets = ParseThemePresets(root);
        var index = presets.FindIndex(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) presets[index] = preset;
        else presets.Add(preset);
        WriteThemePresets(root, presets);
    }

    public static void DeleteThemePreset(string name)
    {
        var root = ReadDocument();
        var presets = ParseThemePresets(root);
        presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        WriteThemePresets(root, presets);
    }

    private static void WriteThemePresets(JsonObject root, List<NamedThemePreset> presets)
    {
        root["version"] = 1;
        root["themePresets"] = System.Text.Json.JsonSerializer.SerializeToNode(presets,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        SaveConfigurationText(root.ToJsonString(new() { WriteIndented = true }));
    }

    public static void SaveLanguage(string language)
    {
        if (language is not ("auto" or "en" or "zh-CN"))
            throw new ArgumentException("Language must be auto, en or zh-CN.");
        var root = ReadDocument();
        root["version"] = 1;
        root["language"] = language;
        WritePrivate(SettingsPath, root.ToJsonString(new() { WriteIndented = true }) + "\n");
    }

    public static LyricsSettings LoadLyrics() => ParseLyrics(ReadDocument());

    private static LyricsSettings ParseLyrics(JsonObject root)
    {
        var lyrics = root["lyrics"];
        var settings = new LyricsSettings(lyrics?["prefetch"]?.GetValue<bool>() ?? true,
            lyrics?["prefetchCount"]?.GetValue<int>() ?? 5,
            lyrics?["searchStrategy"]?.GetValue<string>() ?? "fallback",
            lyrics?["matchMode"]?.GetValue<string>() ?? "balanced",
            lyrics?["playerSource"]?.GetValue<bool>() ?? true,
            lyrics?["qqMusicSource"]?.GetValue<bool>() ?? true,
            lyrics?["neteaseSource"]?.GetValue<bool>() ?? true,
            lyrics?["preferredSource"]?.GetValue<string>() ?? "qq");
        ValidateLyrics(settings);
        return settings;
    }

    private static void ValidateLyrics(LyricsSettings settings)
    {
        if (settings.PrefetchCount is < 1 or > 20)
            throw new ArgumentException("Prefetch count must be between 1 and 20.");
        if (settings.SearchStrategy is not ("quick" or "fallback")
            || settings.MatchMode is not ("balanced" or "strict")
            || settings.PreferredSource is not ("qq" or "netease"))
            throw new ArgumentException("Unknown lyric search strategy, match mode or preferred source.");
        if (!settings.PlayerSource && !settings.QQMusicSource && !settings.NeteaseSource)
            throw new ArgumentException("Enable at least one lyric source.");
    }

    public static void SaveLyrics(LyricsSettings settings)
    {
        ValidateLyrics(settings);
        var root = ReadDocument();
        root["version"] = 1;
        if (root["lyrics"] is not JsonObject) root["lyrics"] = new JsonObject();
        root["lyrics"]!["prefetch"] = settings.Prefetch;
        root["lyrics"]!["prefetchCount"] = settings.PrefetchCount;
        root["lyrics"]!["searchStrategy"] = settings.SearchStrategy;
        root["lyrics"]!["matchMode"] = settings.MatchMode;
        root["lyrics"]!["playerSource"] = settings.PlayerSource;
        root["lyrics"]!["qqMusicSource"] = settings.QQMusicSource;
        root["lyrics"]!["neteaseSource"] = settings.NeteaseSource;
        root["lyrics"]!["preferredSource"] = settings.PreferredSource;
        WritePrivate(SettingsPath, root.ToJsonString(new() { WriteIndented = true }) + "\n");
    }
    public static bool HasEnvironmentOverride => new[] { "CIDER_AUTH_MODE", "CIDER_API_TOKEN", "CIDER_TOKEN_FILE" }
        .Any(key => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)));

    public static CiderConnectionSettings LoadCider() => ParseCider(ReadDocument());

    private static CiderConnectionSettings ParseCider(JsonObject root)
    {
        var mode = root["cider"]?["authentication"]?.GetValue<string>();
        if (mode is null)
            mode = HasConfiguredToken() ? "token" : "none";
        ValidateMode(mode);
        var integration = root["cider"]?["integration"]?.GetValue<string>() ?? "auto";
        ValidateIntegration(integration);
        return new(mode, integration);
    }

    public static string EffectiveAuthentication()
    {
        var mode = Environment.GetEnvironmentVariable("CIDER_AUTH_MODE");
        if (string.IsNullOrWhiteSpace(mode)) return LoadCider().Authentication;
        ValidateMode(mode);
        return mode;
    }

    public static string? ResolveCiderToken()
    {
        if (EffectiveAuthentication() == "none") return null;
        var direct = Environment.GetEnvironmentVariable("CIDER_API_TOKEN");
        if (!string.IsNullOrWhiteSpace(direct)) return ValidateToken(direct.Trim());
        var customPath = Environment.GetEnvironmentVariable("CIDER_TOKEN_FILE");
        return ReadTokenFile(string.IsNullOrWhiteSpace(customPath) ? TokenPath : customPath);
    }

    public static string? ReadSavedToken() => ReadTokenFile(TokenPath);

    public static void SaveCider(string authentication, string? newToken = null, string? integration = null)
    {
        ValidateMode(authentication);
        if (integration != null) ValidateIntegration(integration);
        // Read/validate before touching credentials so malformed settings are not overwritten.
        var root = ReadDocument();
        if (!string.IsNullOrWhiteSpace(newToken))
            WritePrivate(TokenPath, ValidateToken(newToken.Trim()) + "\n");
        root["version"] = 1;
        if (root["cider"] is not JsonObject) root["cider"] = new JsonObject();
        root["cider"]!["authentication"] = authentication;
        if (integration != null) root["cider"]!["integration"] = integration;
        WritePrivate(SettingsPath, root.ToJsonString(new() { WriteIndented = true }) + "\n");
    }

    public static ServerSettings LoadServer()
    {
        var server = ReadDocument()["server"];
        var settings = new ServerSettings(
            Environment.GetEnvironmentVariable("OMNILYRICS_LISTEN_ADDRESS") ?? server?["listenAddress"]?.GetValue<string>() ?? "127.0.0.1",
            EnvironmentPort("OMNILYRICS_HTTP_PORT", server?["httpPort"]?.GetValue<int>() ?? 27270),
            EnvironmentPort("OMNILYRICS_UDP_PORT", server?["udpPort"]?.GetValue<int>() ?? 32651),
            Environment.GetEnvironmentVariable("OMNILYRICS_CONTROL_HOST") ?? server?["controlHost"]?.GetValue<string>() ?? "127.0.0.1");
        ValidateServer(settings);
        return settings;
    }

    public static void SaveServer(ServerSettings settings)
    {
        ValidateServer(settings);
        var root = ReadDocument();
        root["version"] = 1;
        if (root["server"] is not JsonObject) root["server"] = new JsonObject();
        var server = root["server"]!;
        server["listenAddress"] = settings.ListenAddress;
        server["httpPort"] = settings.HttpPort;
        server["udpPort"] = settings.UdpPort;
        server["controlHost"] = settings.ControlHost;
        WritePrivate(SettingsPath, root.ToJsonString(new() { WriteIndented = true }) + "\n");
    }

    public static void ValidateServer(ServerSettings settings)
    {
        if (!IPAddress.TryParse(settings.ListenAddress, out _))
            throw new ArgumentException("The listen address must be an IPv4 or IPv6 address.");
        if (settings.HttpPort is < 1 or > 65535 || settings.UdpPort is < 1 or > 65535)
            throw new ArgumentException("Ports must be between 1 and 65535.");
        if (Uri.CheckHostName(settings.ControlHost) == UriHostNameType.Unknown
            || settings.ControlHost is "0.0.0.0" or "::")
            throw new ArgumentException("The control host must be a destination address or hostname, not a wildcard.");
    }

    private static int EnvironmentPort(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (int.TryParse(value, out var port)) return port;
        throw new ArgumentException("The port environment variable must contain an integer.");
    }

    private static void ValidateIntegration(string integration)
    {
        if (integration is not ("auto" or "webapi" or "mpris"))
            throw new InvalidDataException("Cider integration must be auto, webapi or mpris.");
    }

    private static JsonObject ReadDocument()
    {
        if (!File.Exists(SettingsPath)) return new JsonObject();
        return JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject
            ?? throw new InvalidDataException("The configuration must be a JSON object.");
    }

    public static string ReadConfigurationText() => File.Exists(SettingsPath)
        ? File.ReadAllText(SettingsPath) : "{\n  \"version\": 1\n}\n";

    public static void ValidateConfigurationText(string text)
    {
        var root = JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException("The configuration must be a JSON object.");
        if (root["version"] is { } version && version.GetValue<int>() != 1)
            throw new InvalidDataException("Unsupported configuration version.");
        var language = root["language"]?.GetValue<string>() ?? "auto";
        if (language is not ("auto" or "en" or "zh-CN")) throw new InvalidDataException("Unknown language.");
        ParseAppearance(root); ParseLyrics(root); ParseCider(root); ParseThemePresets(root); ParseSpotifyClientId(root);
        var server = root["server"];
        ValidateServer(new(server?["listenAddress"]?.GetValue<string>() ?? "127.0.0.1",
            server?["httpPort"]?.GetValue<int>() ?? 27270, server?["udpPort"]?.GetValue<int>() ?? 32651,
            server?["controlHost"]?.GetValue<string>() ?? "127.0.0.1"));
    }

    public static void SaveConfigurationText(string text)
    {
        ValidateConfigurationText(text);
        WritePrivate(SettingsPath, text.TrimEnd() + "\n");
    }

    public static void SavePreferences(AppearanceSettings appearance, LyricsSettings lyrics,
        CiderConnectionSettings? cider = null, string? newToken = null)
    {
        var root = ReadDocument();
        root["version"] = 1;
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        void Merge(string key, JsonObject values)
        {
            if (root[key] is not JsonObject) root[key] = new JsonObject();
            foreach (var value in values) root[key]![value.Key] = value.Value?.DeepClone();
        }
        Merge("appearance", (JsonObject)System.Text.Json.JsonSerializer.SerializeToNode(appearance, options)!);
        Merge("lyrics", (JsonObject)System.Text.Json.JsonSerializer.SerializeToNode(lyrics, options)!);
        if (cider != null) Merge("cider", (JsonObject)System.Text.Json.JsonSerializer.SerializeToNode(cider, options)!);
        var text = root.ToJsonString(new() { WriteIndented = true }) + "\n";
        ValidateConfigurationText(text);
        if (!string.IsNullOrWhiteSpace(newToken)) WritePrivate(TokenPath, ValidateToken(newToken.Trim()) + "\n");
        WritePrivate(SettingsPath, text);
    }

    private static bool HasConfiguredToken() => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CIDER_API_TOKEN"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CIDER_TOKEN_FILE")) || File.Exists(TokenPath);

    private static string? ReadTokenFile(string path)
    {
        if (!File.Exists(path)) return null;
        var token = File.ReadAllText(path).Trim();
        return token.Length == 0 ? null : ValidateToken(token);
    }

    private static string ValidateToken(string token)
    {
        if (token.Contains('\r') || token.Contains('\n'))
            throw new InvalidDataException("The token must be a single line.");
        return token;
    }

    private static void ValidateMode(string mode)
    {
        if (mode is not ("token" or "none"))
            throw new InvalidDataException("Cider authentication must be token or none.");
    }

    private static void WritePrivate(string path, string content)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DirectoryPath);
        else Directory.CreateDirectory(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temporary = Path.Combine(DirectoryPath, ".write-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            using (var writer = new StreamWriter(stream)) writer.Write(content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
