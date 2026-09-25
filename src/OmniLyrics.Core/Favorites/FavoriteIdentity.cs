namespace OmniLyrics.Core.Favorites;

internal static class FavoriteIdentity
{
    internal static bool Matches(PlayerState expected, string? title, IEnumerable<string> artists, string? album, double durationMs)
    {
        static string Norm(string? value) => (value ?? "").Trim().ToUpperInvariant();
        return !string.IsNullOrWhiteSpace(expected.Title) && Norm(expected.Title) == Norm(title)
            && Norm(string.Join(", ", expected.Artists)) == Norm(string.Join(", ", artists))
            && (string.IsNullOrEmpty(expected.Album) || Norm(expected.Album) == Norm(album))
            && (expected.Duration == TimeSpan.Zero || Math.Abs(expected.Duration.TotalMilliseconds - durationMs) < 2000);
    }
}
