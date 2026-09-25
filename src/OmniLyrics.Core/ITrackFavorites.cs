namespace OmniLyrics.Core;

/// <summary>Optional library capability. A null state means unavailable for this track/connection.</summary>
public interface ITrackFavorites
{
    Task<FavoriteState?> GetFavoriteAsync(PlayerState expected, CancellationToken token = default);
    Task<FavoriteState?> SetFavoriteAsync(PlayerState expected, FavoriteState previous, bool favorite, CancellationToken token = default);
}

public sealed record FavoriteState(string TrackId, string MediaKey, bool IsFavorite);
public sealed record FavoriteRequest(FavoriteState Previous, bool Favorite);
