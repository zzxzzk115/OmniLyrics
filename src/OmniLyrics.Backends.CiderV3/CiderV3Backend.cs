using OmniLyrics.Core;

namespace OmniLyrics.Backends.CiderV3;

public class CiderV3Backend : BasePlayerBackend
{
    private readonly CiderV3Api _api = CiderV3Api.CreateDefault();
    private PlayerState? _lastState;
    private CancellationTokenSource? _cts;

    public override PlayerState? GetCurrentState() => _lastState;

    public override Task StartAsync(CancellationToken token)
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
                EmitStateChanged(null!);
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
            EmitStateChanged(state);
    }

    private static bool StatesEqual(PlayerState? a, PlayerState b)
    {
        if (a is null) return false;

        if (a.Artists.Count != b.Artists.Count)
            return false;

        for (int i = 0; i < a.Artists.Count; i++)
            if (a.Artists[i] != b.Artists[i])
                return false;

        return a.Title == b.Title &&
               a.Album == b.Album &&
               a.Duration == b.Duration &&
               a.Playing == b.Playing &&
               a.SourceApp == b.SourceApp;
    }

    public override Task PlayAsync() => _api.PlayAsync();
    public override Task PauseAsync() => _api.PauseAsync();
    public override Task TogglePlayPauseAsync() => _api.ToggleAsync();
    public override Task NextAsync() => _api.NextAsync();
    public override Task PreviousAsync() => _api.PreviousAsync();
    public override Task SeekAsync(TimeSpan position) => _api.SeekAsync(position);
}