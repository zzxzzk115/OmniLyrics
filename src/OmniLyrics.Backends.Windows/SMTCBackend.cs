using OmniLyrics.Core;
using OmniLyrics.Core.Helpers;
using Windows.Media.Control;
using WindowsMediaController;

namespace OmniLyrics.Backends.Windows;

/// <summary>
/// SMTC (System Media Transport Controls) backend for media playback and control. Only works on Windows.
/// </summary>
public class SMTCBackend : BasePlayerBackend
{
    private readonly MediaManager _mediaManager = new();
    private MediaManager.MediaSession? _currentSession;
    private PlayerState? _lastState;

    private YesPlayMusicApi _yesPlayMusicApi = new();

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _mediaManager.OnFocusedSessionChanged += HandleFocusedSessionChanged;
        _mediaManager.OnAnyMediaPropertyChanged += HandleMediaPropertyChanged;
        _mediaManager.OnAnyTimelinePropertyChanged += HandleTimelineChanged;
        _mediaManager.OnAnyPlaybackStateChanged += HandlePlaybackChanged;

        try
        {
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
        catch
        {
            // ignored
        }
    }

    public override PlayerState? GetCurrentState() => _lastState;

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
        try
        {
            var sessions = _mediaManager.CurrentMediaSessions;

            // Try get playing session
            foreach (var session in sessions)
            {
                var playbackInfo = session.Value.ControlSession.GetPlaybackInfo();
                if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    _currentSession = session.Value;
                    break;
                }
            }

            if (_currentSession == null)
                return;

            // Skip Cider
            if (_currentSession.ControlSession.SourceAppUserModelId.StartsWith("Cider"))
            {
                // Select aother non-Cider session
                foreach (var session in sessions)
                {
                    if (session.Value.ControlSession.SourceAppUserModelId.StartsWith("Cider"))
                        continue;

                    _currentSession = session.Value;
                    break;
                }

                // Select failed, return
                if (_currentSession.ControlSession.SourceAppUserModelId.StartsWith("Cider"))
                {
                    return;
                }
            }

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

            // Try get player state from YesPlayMusic
            if (control.SourceAppUserModelId.StartsWith("YesPlayMusic"))
            {
                var ypState = await _yesPlayMusicApi.GetStateAsync();
                if (ypState != null)
                {
                    // When YesPlayMusic skipped some songs...
                    if (ypState.Title != media.Title)
                    {
                        state = ypState.DeepCopy();
                    }

                    // Override position anyway
                    state.Position = ypState.Position;
                }
            }

            var thumb = media.Thumbnail;
            if (thumb != null)
            {
                using var ras = await thumb.OpenReadAsync();
                using var stream = ras.AsStreamForRead();
                using var img = SixLabors.ImageSharp.Image.Load(stream);

                state.ArtworkWidth = img.Width;
                state.ArtworkHeight = img.Height;
            }

            if (!StatesEqual(_lastState, state))
            {
                _lastState = state;
                EmitStateChanged(state);
            }
        }
        catch
        {
            // ignored
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

    public override async Task PlayAsync()
    {
        var s = EnsureSession();
        if (s != null)
            await s.TryPlayAsync();
    }

    public override async Task PauseAsync()
    {
        var s = EnsureSession();
        if (s != null)
            await s.TryPauseAsync();
    }

    public override async Task TogglePlayPauseAsync()
    {
        var s = EnsureSession();
        if (s != null)
            await s.TryTogglePlayPauseAsync();
    }

    public override async Task NextAsync()
    {
        var s = EnsureSession();
        if (s != null)
            await s.TrySkipNextAsync();
    }

    public override async Task PreviousAsync()
    {
        var s = EnsureSession();
        if (s != null)
            await s.TrySkipPreviousAsync();
    }

    public override async Task SeekAsync(TimeSpan position)
    {
        var s = EnsureSession();
        if (s != null)
        {
            await s.TryChangePlaybackPositionAsync(position.Ticks);
        }
    }
}