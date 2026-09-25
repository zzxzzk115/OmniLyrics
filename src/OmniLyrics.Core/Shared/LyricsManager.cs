using OmniLyrics.Core.Lyrics;
using OmniLyrics.Core.Lyrics.Models;

namespace OmniLyrics.Core.Shared;

public class LyricsManager
{
    public LyricsCache Cache { get; }
    private readonly object _gate = new();

    private string _lastId = "";
    private bool _lastKaraoke;
    private string _lastPolicy = "";
    private long _revision;

    public LyricsManager(Func<PlayerState, bool, Task<List<LyricsLine>?>>? loadLyrics = null, LyricsCache? cache = null)
    {
        Cache = cache ?? (loadLyrics == null ? LyricsCache.Shared : new LyricsCache(loadLyrics));
    }

    public List<LyricsLine>? Current { get; private set; }
    public bool IsLoading { get; private set; }

    private static string TrackId(PlayerState state) => LyricsCache.TrackKey(state);

    public bool IsCurrentTrack(PlayerState? state)
    {
        var policy = Cache.SearchIdentity;
        lock (_gate) return state != null && _lastId == TrackId(state) && _lastPolicy == policy;
    }

    public (List<LyricsLine>? Lines, bool Loading, long Revision) Capture(PlayerState? state)
    {
        var policy = Cache.SearchIdentity;
        lock (_gate)
            return state != null && _lastId == TrackId(state) && _lastPolicy == policy
                ? (Current, IsLoading, _revision) : (null, false, _revision);
    }

    public async Task UpdateAsync(PlayerState? state, bool karaoke)
    {
        string id = state == null ? "" : TrackId(state);
        var policy = Cache.SearchIdentity;
        long requestRevision;
        lock (_gate)
        {
            if (id == _lastId && karaoke == _lastKaraoke && policy == _lastPolicy
                && (state == null || IsLoading || Cache.IsFresh(state, karaoke))) return;
            _lastId = id;
            _lastKaraoke = karaoke;
            _lastPolicy = policy;
            _revision++;
            requestRevision = _revision;
            Current = null;
            IsLoading = false;
            if (state == null || string.IsNullOrWhiteSpace(state.Title)
                || string.IsNullOrWhiteSpace(state.Artists.FirstOrDefault())) return;
            IsLoading = true;
        }
        // Providers may normalize artists; never mutate the player's live metadata.
        var parsed = await Cache.GetAsync(state, karaoke);
        lock (_gate)
        {
            if (_lastId == id && _lastKaraoke == karaoke && _lastPolicy == policy && Cache.SearchIdentity == policy && _revision == requestRevision)
            {
                Current = parsed;
                IsLoading = false;
                _revision++;
            }
        }
    }
}
