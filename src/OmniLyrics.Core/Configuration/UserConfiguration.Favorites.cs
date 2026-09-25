using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmniLyrics.Core.Configuration;

public sealed record SpotifyTokens(string ClientId, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string Scope);

public static partial class UserConfiguration
{
    private static readonly JsonSerializerOptions FavoritesJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string SpotifyTokenPath => Path.Combine(DirectoryPath, "spotify-authorization.json");
    private static string YesPlayMusicCookiePath => Path.Combine(DirectoryPath, "yesplaymusic-cookie");
    public static string LoadSpotifyClientId() => ParseSpotifyClientId(ReadDocument());
    private static string ParseSpotifyClientId(JsonObject root)
    {
        var id = root["spotify"]?["clientId"]?.GetValue<string>()?.Trim() ?? "";
        if (id.Length > 256 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InvalidDataException("Invalid Spotify Client ID.");
        return id;
    }
    public static void SaveSpotifyClientId(string value)
    {
        var root = ReadDocument();
        root["spotify"] = new JsonObject { ["clientId"] = value.Trim() };
        var id = ParseSpotifyClientId(root);
        WritePrivate(SettingsPath, root.ToJsonString(new() { WriteIndented = true }) + "\n");
        if (ReadSpotifyTokens() is { } tokens && tokens.ClientId != id) ClearSpotifyTokens();
    }
    public static SpotifyTokens? ReadSpotifyTokens()
    {
        try
        {
            var value = File.Exists(SpotifyTokenPath) ? JsonSerializer.Deserialize<SpotifyTokens>(File.ReadAllText(SpotifyTokenPath), FavoritesJson) : null;
            return value != null && !string.IsNullOrWhiteSpace(value.ClientId) && !string.IsNullOrWhiteSpace(value.AccessToken)
                && !string.IsNullOrWhiteSpace(value.RefreshToken) && !string.IsNullOrWhiteSpace(value.Scope) ? value : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    public static void SaveSpotifyTokens(SpotifyTokens tokens) => WritePrivate(SpotifyTokenPath, JsonSerializer.Serialize(tokens, FavoritesJson));
    public static void ClearSpotifyTokens() { if (File.Exists(SpotifyTokenPath)) File.Delete(SpotifyTokenPath); }
    public static string? ReadYesPlayMusicCookie()
    {
        try { return File.Exists(YesPlayMusicCookiePath) ? File.ReadAllText(YesPlayMusicCookiePath).Trim() : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
    public static void SaveYesPlayMusicCookie(string cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie) || cookie.Length > 32768 || cookie.Any(c => c is '\r' or '\n'))
            throw new InvalidDataException("Invalid YesPlayMusic login cookie.");
        WritePrivate(YesPlayMusicCookiePath, cookie);
    }
    public static void ClearYesPlayMusicCookie() { if (File.Exists(YesPlayMusicCookiePath)) File.Delete(YesPlayMusicCookiePath); }
}
