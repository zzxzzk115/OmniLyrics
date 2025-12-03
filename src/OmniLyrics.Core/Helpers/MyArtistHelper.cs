namespace OmniLyrics.Core.Helpers;

public static class MyArtistHelper
{
    public static List<string> GetArtistsFromString(string artistDesc)
    {
        // Multiple artists (YesPlayMusic Style)
        if (artistDesc.Contains(","))
        {
            return SplitArtistsFromString(",", artistDesc);
        }

        // Multiple artists (Apple Music Style)
        if (artistDesc.Contains("&"))
        {
            return SplitArtistsFromString("&", artistDesc);
        }

        // Default, only one artist
        return new List<string> { artistDesc };
    }

    private static List<string> SplitArtistsFromString(string separator, string artistDesc)
    {
        var result = artistDesc.Split(separator).ToList();
        result.ForEach(a => a.Trim()); // Trim
        return result;
    }
}