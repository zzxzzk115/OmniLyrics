using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Core.Shared;

/// <summary>Refresh a small upcoming window, without changing displayed lyrics.</summary>
public sealed class LyricsPrefetcher : IDisposable
{
    private readonly CancellationTokenSource _stop;

    public LyricsPrefetcher(IPlayerBackend backend, LyricsManager manager, CancellationToken token,
        Func<bool>? enabled = null)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (backend is IPlaybackQueueSource source)
            _ = RunAsync(backend, source, manager, enabled ?? (() => true), _stop.Token);
    }

    private static async Task RunAsync(IPlayerBackend backend, IPlaybackQueueSource source,
        LyricsManager manager, Func<bool> enabled, CancellationToken token)
    {
        string? lastTrack = null;
        var refreshAt = DateTimeOffset.MinValue;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var state = backend.GetCurrentState();
                var key = state == null ? null : LyricsCache.TrackKey(state);
                if (enabled() && state != null && manager.IsCurrentTrack(state) && !manager.IsLoading
                    && (key != lastTrack || DateTimeOffset.UtcNow >= refreshAt))
                {
                    lastTrack = key;
                    refreshAt = DateTimeOffset.UtcNow.AddSeconds(15);
                    try
                    {
                        var settings = UserConfiguration.LoadLyrics();
                        if (!settings.Prefetch) { await Task.Delay(500, token); continue; }
                        var upcoming = await source.GetUpcomingTracksAsync(settings.PrefetchCount, token);
                        foreach (var track in upcoming.Take(settings.PrefetchCount).DistinctBy(LyricsCache.TrackKey))
                        {
                            var current = backend.GetCurrentState();
                            if (!enabled() || !UserConfiguration.LoadLyrics().Prefetch || manager.IsLoading || current == null
                                || LyricsCache.TrackKey(current) != key || DateTimeOffset.UtcNow >= refreshAt) break;
                            // Already running fetches can finish in the cache, but
                            // obsolete queued tracks never replace the active song.
                            await manager.Cache.PrefetchAsync(track, token);
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch { /* Unavailable queue/auth must not interrupt playback. */ }
                }
                await Task.Delay(500, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose() { _stop.Cancel(); _stop.Dispose(); }
}
