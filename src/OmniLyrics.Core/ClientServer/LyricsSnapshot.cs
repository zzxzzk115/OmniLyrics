using OmniLyrics.Core.Lyrics.Models;

namespace OmniLyrics.Core;

/// <summary>A matching player/lyric snapshot, including real syllable timing.</summary>
public sealed record LyricsSnapshot(string Service, int ProtocolVersion, PlayerState? State,
    List<LyricsLine>? Lyrics, bool Loading, string LyricsVersion, string? OwnerRole = null)
{
    public const string ServiceName = "OmniLyrics";
    public const int CurrentProtocol = 1;
}
