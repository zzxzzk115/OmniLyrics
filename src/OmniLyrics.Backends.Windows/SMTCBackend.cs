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

    private DateTime _lastSyncTime;
    private TimeSpan _lastSyncPosition;
    private bool _isPlaying;

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
                    PushPositionTick(); // internal timeline update
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

    // --- Sync SMTC position with internal tracker ---
    private void SyncPosition(TimeSpan pos, bool playing)
    {
        _lastSyncTime = DateTime.UtcNow;
        _lastSyncPosition = pos;
        _isPlaying = playing;
    }

    // --- Predictive timeline tick ---
    private void PushPositionTick()
    {
        if (_lastState == null || !_isPlaying)
            return;

        var elapsed = DateTime.UtcNow - _lastSyncTime;
        if (elapsed.TotalMilliseconds < 0)
            return;

        var predicted = _lastSyncPosition + elapsed;

        if (_lastState.Duration != TimeSpan.Zero && predicted > _lastState.Duration)
            predicted = _lastState.Duration;

        // Only update if the drift is small (SMTC didn't report a jump)
        if (Math.Abs((predicted - _lastState.Position).TotalMilliseconds) < 500)
        {
            _lastState.Position = predicted;
            EmitStateChanged(_lastState);
        }
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
                var artists = parts[0];
                mediaArtists = MyArtistHelper.GetArtistsFromString(artists) ?? mediaArtists;
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
                mediaArtists = MyArtistHelper.GetArtistsFromString(media.Artist!);
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

                    // Override position, duration, artwork anyway
                    state.Position = ypState.Position;
                    state.Duration = ypState.Duration;
                    state.ArtworkUrl = ypState.ArtworkUrl;
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

            // Seek detection: if SMTC jumps significantly
            if (_lastState != null)
            {
                var diff = Math.Abs((state.Position - _lastState.Position).TotalMilliseconds);
                if (diff > 500)
                {
                    // I resync position on seek
                    SyncPosition(state.Position, state.Playing);
                }
            }
            else
            {
                // First-time sync
                SyncPosition(state.Position, state.Playing);
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