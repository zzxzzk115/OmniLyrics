namespace OmniLyrics.Core;

/// <summary>
/// Media player state.
/// </summary>
public class PlayerState
{
    public string? Title { get; set; }
    public List<string> Artists { get; set; } = new();
    public string? Album { get; set; }
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Playing { get; set; }
    public string? SourceApp { get; set; }
    public string? ArtworkUrl { get; set; }
    public int ArtworkWidth { get; set; }
    public int ArtworkHeight { get; set; }

    /// <summary>
    /// Creates a full deep copy of this state (safe for overlays and multi-backend updates).
    /// </summary>
    public PlayerState DeepCopy()
    {
        return new PlayerState
        {
            Title = this.Title,
            Album = this.Album,
            Position = this.Position,
            Duration = this.Duration,
            Playing = this.Playing,
            SourceApp = this.SourceApp,
            ArtworkUrl = this.ArtworkUrl,
            ArtworkWidth = this.ArtworkWidth,
            ArtworkHeight = this.ArtworkHeight,
            Artists = new List<string>(this.Artists)
        };
    }
}