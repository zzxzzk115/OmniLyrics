using OmniLyrics.Core;

namespace OmniLyrics.Backends.CiderV3;

public class CiderV3Backend : IPlayerBackend
{
    private readonly CiderV3Api _api = CiderV3Api.CreateDefault();
    private PlayerState? _lastState;
    private CancellationTokenSource? _cts;

    public event EventHandler<PlayerState>? OnStateChanged;

    public PlayerState? GetCurrentState() => _lastState;

    public Task StartAsync(CancellationToken token)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _ = PollLoop(_cts.Token);
        return Task.CompletedTask;
    }

    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await QueryAsync(); }
            catch { }

            await Task.Delay(200, token);
        }
    }

    private async Task QueryAsync()
    {
        var info = await _api.TryGetCurrentSongTypedAsync();
        if (info == null)
        {
            if (_lastState != null)
            {
                _lastState = null;
                OnStateChanged?.Invoke(this, null!);
            }
            return;
        }

        var state = new PlayerState
        {
            Title = info.Name ?? "",
            Playing = info.IsPlaying ?? (info.CurrentPlaybackTime > 0),
            Duration = TimeSpan.FromMilliseconds(info.DurationInMillis),
            Position = TimeSpan.FromSeconds(info.CurrentPlaybackTime),
            SourceApp = "Cider"
        };

        if (!string.IsNullOrEmpty(info.ArtistName))
            state.Artists.Add(info.ArtistName);

        if (!string.IsNullOrEmpty(info.AlbumName))
            state.Album = info.AlbumName;

        if (info.Artwork?.Url != null)
            state.ArtworkUrl = info.Artwork.Url;

        bool changed = !StatesEqual(_lastState, state);
        _lastState = state;

        if (changed)
            OnStateChanged?.Invoke(this, state);
    }

    private static bool StatesEqual(PlayerState? a, PlayerState b)
    {
        if (a == null) return false;
        if (a.Title != b.Title) return false;
        if (a.Playing != b.Playing) return false;
        if (a.Duration != b.Duration) return false;
        if (a.Artists.Count != b.Artists.Count) return false;
        if (a.Artists.Count != 0 && a.Artists[0] != b.Artists[0]) return false;
        return true;
    }

    public Task PlayAsync() => _api.PlayAsync();
    public Task PauseAsync() => _api.PauseAsync();
    public Task TogglePlayPauseAsync() => _api.ToggleAsync();
    public Task NextAsync() => _api.NextAsync();
    public Task PreviousAsync() => _api.PreviousAsync();
    public Task SeekAsync(TimeSpan position) => _api.SeekAsync(position);
}