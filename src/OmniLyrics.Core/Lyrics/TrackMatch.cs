using System.Text;
using System.Text.RegularExpressions;
using Lyricify.Lyrics.Helpers.General;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;

namespace OmniLyrics.Core.Lyrics;

/// <summary>Reject incompatible recordings before considering lyric timing quality.</summary>
public static class TrackMatch
{
    private static readonly string[] Versions = [@"\blive\b|现场|演唱会", @"\bremix\b|混音", @"\bacoustic\b|不插电",
        @"\binstrumental\b|伴奏|纯音乐", @"\bcover\b|翻唱", @"\bradio\s+edit\b", @"\bsped\s*up\b|加速", @"\bslowed\b|慢速"];

    public static bool IsSuitable(PlayerState expected, ISearchResult candidate, string matchMode = "balanced")
    {
        if (string.IsNullOrWhiteSpace(expected.Title) || string.IsNullOrWhiteSpace(candidate.Title)) return false;
        var original = expected.Title + " " + expected.Album;
        var found = candidate.Title + " " + candidate.Album;
        if (Versions.Any(pattern => Regex.IsMatch(original, pattern, RegexOptions.IgnoreCase)
            != Regex.IsMatch(found, pattern, RegexOptions.IgnoreCase))) return false;
        if (NormalizeTitle(expected.Title) != NormalizeTitle(candidate.Title)) return false;
        var artists = NormalizeArtists(expected.Artists);
        var resultArtists = NormalizeArtists(candidate.Artists);
        if (artists.Count == 0 || resultArtists.Count == 0 || !artists.Overlaps(resultArtists)) return false;
        if (!NormalizeArtists(expected.Artists.Take(1)).Overlaps(NormalizeArtists(candidate.Artists.Take(1)))) return false;
        if (matchMode == "strict" && (string.IsNullOrWhiteSpace(expected.Album) || string.IsNullOrWhiteSpace(candidate.Album)
            || Normalize(expected.Album) != Normalize(candidate.Album)
            || expected.Duration <= TimeSpan.Zero || candidate.DurationMs is not > 0)) return false;
        if (expected.Duration.TotalSeconds > 0 && candidate.DurationMs is > 0)
        {
            // Small encoding / trailing-silence differences are common. Larger
            // gaps often indicate a live version, edit, or different recording.
            var difference = Math.Abs(expected.Duration.TotalMilliseconds - candidate.DurationMs.Value);
            if (difference > (matchMode == "strict" ? 2000 : Math.Clamp(expected.Duration.TotalMilliseconds * .02, 3000, 6000))) return false;
        }
        return true;
    }

    private static HashSet<string> NormalizeArtists(IEnumerable<string> artists)
    {
        var list = artists.ToList();
        ArtistHelper.ChineselizeArtists(list);
        return list.SelectMany(a => Regex.Split(a, @"\s*(?:,|、|&|;|\bfeat\.?\s|\bfeaturing\s)\s*", RegexOptions.IgnoreCase))
            .Select(Normalize).Where(s => s.Length > 0).ToHashSet();
    }

    private static string NormalizeTitle(string title)
    {
        title = Regex.Replace(title, @"[（(]\s*(?:feat\.?|featuring|with)\s+[^)）]*[)）]", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"(?:[（(]|\s-\s)\s*(?:\d{4}\s+)?remaster(?:ed)?(?:\s+\d{4})?\s*[)）]?", "", RegexOptions.IgnoreCase);
        return Normalize(title);
    }

    private static string Normalize(string text) => new(ChineseHelper.T2S(text).Normalize(NormalizationForm.FormKC)
        .ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
