using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmniLyrics.Core.Configuration;

public sealed record LanSettings(bool Enabled = false, string DeviceName = "", int HttpsPort = 27271,
    int DiscoveryPort = 32652, string? SelectedDeviceId = null);

public static partial class UserConfiguration
{
    private static readonly JsonSerializerOptions LanJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static LanSettings LoadLan() => ParseLan(ReadDocument());
    private static LanSettings ParseLan(JsonObject root)
    {
        var settings = root["lan"]?.Deserialize<LanSettings>(LanJson) ?? new();
        if (settings.HttpsPort is < 1024 or > 65535 || settings.DiscoveryPort is < 1024 or > 65535
            || settings.DeviceName is null || settings.DeviceName.Length > 64 || settings.DeviceName.Any(char.IsControl)
            || settings.SelectedDeviceId is { } id && !Guid.TryParseExact(id, "N", out _))
            throw new InvalidDataException("Invalid LAN settings.");
        return settings;
    }
    public static void SaveLan(LanSettings settings)
    {
        var root = ReadDocument();
        root["lan"] = JsonSerializer.SerializeToNode(settings, LanJson);
        ParseLan(root);
        WritePrivate(SettingsPath, root.ToJsonString(new() { WriteIndented = true }) + "\n");
    }
}
