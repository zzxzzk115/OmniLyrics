namespace NMSL.Core;

public enum MediaType
{
    Music,
    Video,
    Podcast,
    Unknown
}

public static class MediaTypeDetector
{
    public static MediaType Guess(PlayerState state)
    {
        if (state == null)
            return MediaType.Unknown;

        int score = 0;

        // 1) Artist: music almost always has an artist.
        if (state.Artists.Count > 0)
            score += 3;
        else
            score -= 2;

        // 2) Album: music tracks often have album name; video usually doesn't.
        if (!string.IsNullOrWhiteSpace(state.Album))
            score += 2;

        // 3) Duration: very strong classifier
        var dur = state.Duration;

        if (dur.TotalSeconds <= 1)
        {
            // extremely short → ads, UI sound, video fragment
            score -= 3;
        }
        else if (dur.TotalMinutes is >= 2 and <= 7)
        {
            // typical music duration
            score += 3;
        }
        else if (dur.TotalMinutes >= 30)
        {
            // could be podcast or long video
            score -= 1;
        }

        // 4) Artwork aspect ratio: square → music, widescreen → video
        if (state.ArtworkWidth > 0 && state.ArtworkHeight > 0)
        {
            double aspect = (double)state.ArtworkWidth / state.ArtworkHeight;

            if (aspect is > 0.9 and < 1.1)
            {
                // square cover → music
                score += 3;
            }
            else if (aspect > 1.3)
            {
                // widescreen → video
                score -= 3;
            }
        }

        // Final classification
        if (score >= 4)
            return MediaType.Music;

        if (score <= -3)
            return MediaType.Video;

        // duration long, no video-like traits
        if (dur.TotalMinutes >= 25)
            return MediaType.Podcast;

        return MediaType.Unknown;
    }
}