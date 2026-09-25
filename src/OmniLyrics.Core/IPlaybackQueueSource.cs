namespace OmniLyrics.Core;

/// <summary>Optional, read-only access to upcoming tracks in playback order.</summary>
public interface IPlaybackQueueSource
{
    Task<IReadOnlyList<PlayerState>> GetUpcomingTracksAsync(int limit, CancellationToken token = default);
}
