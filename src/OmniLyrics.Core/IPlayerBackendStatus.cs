namespace OmniLyrics.Core;

/// <summary>An actionable connection error, independent of whether a song is selected.</summary>
public interface IPlayerBackendStatus
{
    string? ConnectionError { get; }
}
