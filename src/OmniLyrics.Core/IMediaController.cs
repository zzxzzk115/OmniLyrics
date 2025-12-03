namespace OmniLyrics.Core;

/// <summary>
///     Defines an interface for controlling media playback, including operations such as play, pause,  seeking, and
///     navigating between media items.
/// </summary>
/// <remarks>
///     This interface provides asynchronous methods for controlling media playback. Implementations  of this
///     interface are expected to handle media playback state transitions and navigation  between media items. All methods
///     return <see cref="Task" /> to support asynchronous execution.
/// </remarks>
public interface IMediaController
{
    /// <summary>
    ///     Play media.
    /// </summary>
    /// <returns></returns>
    Task PlayAsync();

    /// <summary>
    ///     Pause media.
    /// </summary>
    /// <returns></returns>
    Task PauseAsync();

    /// <summary>
    ///     Toggle play/pause.
    /// </summary>
    /// <returns></returns>
    Task TogglePlayPauseAsync();

    /// <summary>
    ///     Switch to next media.
    /// </summary>
    /// <returns></returns>
    Task NextAsync();

    /// <summary>
    ///     Switch to previous media.
    /// </summary>
    /// <returns></returns>
    Task PreviousAsync();

    /// <summary>
    ///     Get media position.
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    Task SeekAsync(TimeSpan position);
}