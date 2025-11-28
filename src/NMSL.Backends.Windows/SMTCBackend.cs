using NMSL.Core;
using Windows.Media.Control;
using WindowsMediaController;

namespace NMSL.Backends.Windows;

/// <summary>
/// SMTC (System Media Transport Controls) backend for media playback and control. Only works on Windows.
/// </summary>
public class SMTCBackend : IPlayerBackend
{
    private readonly MediaManager _mediaManager = new();
    private MediaManager.MediaSession? _currentSession;
    private PlayerState? _lastState;

    public event EventHandler<PlayerState>? OnStateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _mediaManager.OnFocusedSessionChanged += HandleFocusedSessionChanged;
        _mediaManager.OnAnyMediaPropertyChanged += HandleMediaPropertyChanged;
        _mediaManager.OnAnyTimelinePropertyChanged += HandleTimelineChanged;
        _mediaManager.OnAnyPlaybackStateChanged += HandlePlaybackChanged;

        await _mediaManager.StartAsync();

        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PushState();
                await Task.Delay(200, cancellationToken);
            }
        }, cancellationToken);
    }

    public PlayerState? GetCurrentState() => _lastState;

    private void HandleFocusedSessionChanged(MediaManager.MediaSession? session)
    {
        _currentSession = session;
        PushState();
    }

    private void HandleMediaPropertyChanged(MediaManager.MediaSession session, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProps)
    {
        PushState();
    }

    private void HandleTimelineChanged(MediaManager.MediaSession session, GlobalSystemMediaTransportControlsSessionTimelineProperties timeline)
    {
        PushState();
    }

    private void HandlePlaybackChanged(MediaManager.MediaSession session, GlobalSystemMediaTransportControlsSessionPlaybackInfo info)
    {
        PushState();
    }

    private async void PushState()
    {
        if (_currentSession?.ControlSession == null)
            return;

        var control = _currentSession.ControlSession;

        var playback = control.GetPlaybackInfo();
        var timeline = control.GetTimelineProperties();

        // Get media properties
        var media = await control.TryGetMediaPropertiesAsync();

        // Consider Apple Music, Artist = ArtistName1 & ArtistName2 — Album/Title( - Single)
        var mediaArtists = new List<string>();
        var mediaAlbum = media.AlbumTitle;
        if (media.Artist != null && media.Artist.Contains(" — "))
        {
            var parts = media.Artist.Split(" — ");
            var artists = parts[0].Trim().Split("&");
            foreach (var artist in artists)
            {
                mediaArtists.Add(artist.Trim());
            }
            if (string.IsNullOrEmpty(media.AlbumTitle))
            {
                mediaAlbum = parts[1].Trim();
                var more = mediaAlbum.Split(" - ");
                if (more.Length == 1) // Ignore Single EP.
                {
                    mediaAlbum = more[0].Trim();

                    // If album equals title, clear it.
                    if (mediaAlbum.Equals(media.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        mediaAlbum = string.Empty;
                    }
                }
            }
        }
        else
        {
            mediaArtists.Add(media.Artist!);
        }

        var state = new PlayerState
        {
            Title = media.Title,
            Artists = mediaArtists,
            Album = mediaAlbum,
            Playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            Position = timeline.Position,
            Duration = timeline.EndTime - timeline.StartTime,
            SourceApp = control.SourceAppUserModelId,
            ArtworkUrl = null,          // SMTC doesn't provide URL
            ArtworkWidth = 0,           // will be filled below
            ArtworkHeight = 0
        };

        // Extract artwork size
        try
        {
            var thumb = media.Thumbnail;
            if (thumb != null)
            {
                using var ras = await thumb.OpenReadAsync();
                using var stream = ras.AsStreamForRead();
                using var img = SixLabors.ImageSharp.Image.Load(stream);

                state.ArtworkWidth = img.Width;
                state.ArtworkHeight = img.Height;
            }
        }
        catch
        {
            // ignore thumbnail errors (common for some apps)
        }

        if (!StatesEqual(_lastState, state))
        {
            _lastState = state;
            OnStateChanged?.Invoke(this, state);
        }
    }

    private static bool StatesEqual(PlayerState? a, PlayerState b)
    {
        if (a is null) return false;

        if (a.Artists.Count != b.Artists.Count)
            return false;

        for (int i = 0; i < a.Artists.Count; i++)
        {
            if (a.Artists[i] != b.Artists[i])
                return false;
        }

        return a.Title == b.Title &&
               a.Position == b.Position &&
               a.Duration == b.Duration &&
               a.Playing == b.Playing &&
               a.SourceApp == b.SourceApp;
    }

    private GlobalSystemMediaTransportControlsSession? EnsureSession()
    {
        if (_currentSession?.ControlSession != null)
            return _currentSession.ControlSession;

        _mediaManager.ForceUpdate();

        return _mediaManager.GetFocusedSession()?.ControlSession;
    }

    public async Task PlayAsync()
    {
        var s = EnsureSession();
        if (s != null)
            await s.TryPlayAsync();
    }

    public async Task PauseAsync()
    {
        var s = EnsureSession();
        if (s != null)
            await s.TryPauseAsync();
    }

    public async Task TogglePlayPauseAsync()
    {
        var s = EnsureSession();
        if (s != null)
            await s.TryTogglePlayPauseAsync();
    }

    public async Task NextAsync()
    {
        var s = EnsureSession();
        if (s != null)
            await s.TrySkipNextAsync();
    }

    public async Task PreviousAsync()
    {
        var s = EnsureSession();
        if (s != null)
            await s.TrySkipPreviousAsync();
    }

    public async Task SeekAsync(TimeSpan position)
    {
        var s = EnsureSession();
        if (s != null)
        {
            await s.TryChangePlaybackPositionAsync((long)(ulong)position.Ticks);
        }
    }
}