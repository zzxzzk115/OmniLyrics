namespace NMSL.Core;

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
}