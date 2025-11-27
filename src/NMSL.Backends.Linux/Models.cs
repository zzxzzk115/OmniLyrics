namespace NMSL.Backends.Linux;

public class NameOwnerChangedEvent
{
    public string Name { get; set; } = "";
    public string OldOwner { get; set; } = "";
    public string NewOwner { get; set; } = "";
}

public class PlayerMetadata
{
    public string? TrackId { get; }
    public string? Title { get; }
    public IReadOnlyList<string>? Artists { get; }
    public string? Album { get; }
    public IReadOnlyList<string>? Genres { get; }
    public TimeSpan? Length { get; }
    public Uri? ArtUrl { get; }
    public string? Url { get; }

    private PlayerMetadata() { }

    private PlayerMetadata(
            string? trackId,
            string? title,
            IReadOnlyList<string>? artists,
            string? album,
            IReadOnlyList<string>? genres,
            TimeSpan? length,
            Uri? artUrl,
            string? url)
    {
        TrackId = trackId;
        Title = title;
        Artists = artists;
        Album = album;
        Genres = genres;
        Length = length;
        ArtUrl = artUrl;
        Url = url;
    }

    public static PlayerMetadata FromDictionary(IDictionary<string, object> dict)
    {
        string? trackId = null;
        string? title = null;
        List<string>? artists = null;
        string? album = null;
        List<string>? genres = null;
        TimeSpan? length = null;
        Uri? artUrl = null;
        string? url = null;

        foreach (var kv in dict)
        {
            switch (kv.Key)
            {
                case "mpris:trackid":
                    trackId = kv.Value as string;
                    break;
                case "xesam:title":
                    title = kv.Value as string;
                    break;
                case "xesam:artist":
                    if (kv.Value is IEnumerable<object> artistObjs)
                    {
                        artists = artistObjs
                            .OfType<string>()
                            .ToList();
                    }
                    break;
                case "xesam:album":
                    album = kv.Value as string;
                    break;
                case "xesam:genre":
                    if (kv.Value is IEnumerable<object> genreObjs)
                    {
                        genres = genreObjs
                            .OfType<string>()
                            .ToList();
                    }
                    break;
                case "mpris:length":
                    if (kv.Value is long lengthLong)
                    {
                        length = TimeSpan.FromMicroseconds(lengthLong);
                    }
                    break;
                case "mpris:artUrl":
                    if (kv.Value is string artUrlStr && Uri.TryCreate(artUrlStr, UriKind.Absolute, out var artUri))
                    {
                        artUrl = artUri;
                    }
                    break;
                case "xesam:url":
                    url = kv.Value as string;
                    break;
            }
        }

        return new PlayerMetadata(
            trackId,
            title,
            artists,
            album,
            genres,
            length,
            artUrl,
            url
        );
    }
}