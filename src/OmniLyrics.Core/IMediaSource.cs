namespace OmniLyrics.Core;

/// <summary>
/// Represents a media source that can be monitored for state changes and queried for its current state.
/// </summary>
/// <remarks>This interface provides functionality to monitor the state of a media source and retrieve its current
/// state. Implementations of this interface should ensure thread safety for concurrent access to its members.</remarks>
public interface IMediaSource
{
    /// <summary>
    /// Emit on state changed.
    /// </summary>
    event EventHandler<PlayerState>? OnStateChanged;

    /// <summary>
    /// Emit state change
    /// </summary>
    void EmitStateChanged(PlayerState state);

    /// <summary>
    /// Get current media state.
    /// </summary>
    PlayerState? GetCurrentState();

    /// <summary>
    /// Start monitoring media source.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);
}