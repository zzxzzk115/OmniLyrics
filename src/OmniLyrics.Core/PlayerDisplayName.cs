namespace OmniLyrics.Core;

public static class PlayerDisplayName
{
    public static string For(PlayerState? state)
    {
        if (state == null) return Localization.Text("Waiting for player");
        if (!string.IsNullOrWhiteSpace(state.PlayerName)) return state.PlayerName.Trim();
        var source = state.SourceApp ?? "";
        if (source.Contains("cider", StringComparison.OrdinalIgnoreCase)) return "Cider";
        if (source.Contains("yesplaymusic", StringComparison.OrdinalIgnoreCase)) return "YesPlayMusic";
        if (source.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (source == "com.apple.Music") return "Apple Music";
        const string prefix = "org.mpris.MediaPlayer2.";
        if (source.StartsWith(prefix, StringComparison.Ordinal))
            source = source[prefix.Length..].Split('.')[0];
        return string.IsNullOrWhiteSpace(source) ? Localization.Text("Music player") : source;
    }
}
