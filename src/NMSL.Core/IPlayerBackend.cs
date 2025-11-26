namespace NMSL.Core;

/// <summary>
/// Defines the contract for a backend that provides media playback functionality  and control, combining the
/// capabilities of a media source and a media controller.
/// </summary>
/// <remarks>This interface extends both <see cref="IMediaSource"/> and <see cref="IMediaController"/>,  allowing
/// implementations to serve as both a provider of media content and a controller  for playback operations. It is
/// intended to be used in scenarios where unified access  to media content and playback control is required.</remarks>
public interface IPlayerBackend : IMediaSource, IMediaController
{
}