using System.Text.Json.Serialization;

namespace OmniLyrics.Backends.CiderV3;

public class CiderNowPlayingResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("info")]
    public CiderNowPlayingInfo? Info { get; set; }
}

public class CiderNowPlayingInfo
{
    // Basic
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("artistName")]
    public string? ArtistName { get; set; }

    [JsonPropertyName("albumName")]
    public string? AlbumName { get; set; }

    // Playback
    [JsonPropertyName("currentPlaybackTime")]
    public double CurrentPlaybackTime { get; set; }

    [JsonPropertyName("remainingTime")]
    public double RemainingTime { get; set; }

    [JsonPropertyName("durationInMillis")]
    public long DurationInMillis { get; set; }

    [JsonPropertyName("isPlaying")]
    public bool? IsPlaying { get; set; }

    // Artwork
    [JsonPropertyName("artwork")]
    public CiderArtwork? Artwork { get; set; }

    // Genre
    [JsonPropertyName("genreNames")]
    public List<string>? GenreNames { get; set; }

    // Preview audio
    [JsonPropertyName("previews")]
    public List<CiderPreview>? Previews { get; set; }

    // Track metadata
    [JsonPropertyName("trackNumber")]
    public int? TrackNumber { get; set; }

    [JsonPropertyName("discNumber")]
    public int? DiscNumber { get; set; }

    [JsonPropertyName("isrc")]
    public string? Isrc { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    // Official Apple Music URL
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    // Apple Music params
    [JsonPropertyName("playParams")]
    public CiderPlayParams? PlayParams { get; set; }

    // Extra fields not defined above → avoid deserialization errors
    [JsonExtensionData]
    public Dictionary<string, object>? Extra { get; set; }
}

public class CiderArtwork
{
    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class CiderPreview
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class CiderPlayParams
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }
}