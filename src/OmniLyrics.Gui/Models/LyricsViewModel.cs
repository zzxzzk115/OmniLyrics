using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OmniLyrics.Backends.Dynamic;
using OmniLyrics.Core;
using OmniLyrics.Core.Lyrics;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Shared;

namespace OmniLyrics.Gui.Models;

public class LyricsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DesktopSession _backend;
    private readonly CancellationTokenSource _cts = new();
    private readonly LyricsManager _lyrics;
    private readonly LyricsPrefetcher _prefetch;
    private readonly PlaybackClock _clock = new();
    private readonly DispatcherTimer _timer;
    private static readonly LyricsLine Empty = new(TimeSpan.Zero, "", null);
    private LyricsLine _defaultLine = Empty;
    private LyricsLine _noSongLine = new(TimeSpan.Zero, Localization.Get("NoSongPlaying"), null);
    private IReadOnlyList<LyricsLine>? _shownLyrics;
    private string? _requestedTrack;
    private bool _disposed;
    private bool _hasTrack;
    private long _lastSample;

    private string? _title;
    private string _artist = "";
    private string? _album;
    private string? _artworkUrl;
    private TimeSpan _position;
    private TimeSpan _duration;
    private bool _playing;
    private LyricsLine _currentLine = Empty;
    private LyricsLine _secondaryLine = Empty;
    private string _connectionLabel = Localization.Text("Connecting…");
    private string _connectionDetails = "";
    private LyricsLine _previousLine = Empty, _earlierLine = Empty, _laterLine = Empty;
    private TimeSpan _lineEnd;
    private string? _translation;
    private FavoriteState? _favorite;
    private bool _favoriteBusy, _favoriteReading;
    private long _nextFavoriteRead;
    private string? _favoriteKey;
    private string? _favoriteError;
    private PlayerState? _state;

    public LyricsViewModel() : this(new DesktopSession()) { }
    private LyricsViewModel(DesktopSession backend) : this(backend, backend.Lyrics) { }

    public LyricsViewModel(DesktopSession backend, LyricsManager lyrics)
    {
        _backend = backend; _lyrics = lyrics;
        CurrentLine = _noSongLine;
        _ = _backend.StartAsync(_cts.Token);
        _prefetch = new LyricsPrefetcher(_backend, _lyrics, _cts.Token, () => !ReferenceEquals(_lyrics, _backend.Lyrics) && _backend.RemoteSnapshot == null);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Refresh());
        _timer.Start();
    }

    public IPlayerBackend Backend => _backend;
    public string ConnectionLabel { get => _connectionLabel; private set => Set(ref _connectionLabel, value); }
    public string ConnectionDetails { get => _connectionDetails; private set => Set(ref _connectionDetails, value); }
    public ObservableCollection<LyricsLine> Lines { get; } = new();
    public LyricsLine CurrentLine { get => _currentLine; private set => Set(ref _currentLine, value); }
    public LyricsLine SecondaryLine { get => _secondaryLine; private set => Set(ref _secondaryLine, value); }
    public LyricsLine PreviousLine { get => _previousLine; private set => Set(ref _previousLine, value); }
    public LyricsLine EarlierLine { get => _earlierLine; private set => Set(ref _earlierLine, value); }
    public LyricsLine LaterLine { get => _laterLine; private set => Set(ref _laterLine, value); }
    public TimeSpan LineEnd { get => _lineEnd; private set => Set(ref _lineEnd, value); }
    public string? Translation { get => _translation; private set { if (Set(ref _translation, value)) Raise(nameof(HasTranslation)); } }
    public bool HasTranslation => !string.IsNullOrEmpty(Translation);
    public bool ShowTranslations => AppearancePreferences.Current.ShowTranslation;
    public bool Approximate => AppearancePreferences.Current.ApproximateHighlight;
    public string TimingLabel => _shownLyrics?.Count > 0 ? Localization.Get(CurrentLine.Tokens?.Exists(t => t.Duration > TimeSpan.Zero) == true
        ? "WordTiming" : Approximate && LineEnd > CurrentLine.Timestamp ? "ApproximateTiming" : "LineTiming") : "";
    public bool CanControl => _backend.CanControl;
    public bool FavoriteAvailable => _favorite != null && CanControl;
    public bool IsFavorite => _favorite?.IsFavorite == true;
    public bool FavoriteBusy { get => _favoriteBusy; private set { if (Set(ref _favoriteBusy, value)) { Raise(nameof(FavoriteEnabled)); Raise(nameof(FavoriteLabel)); } } }
    public bool FavoriteEnabled => FavoriteAvailable && !FavoriteBusy;
    public string FavoriteLabel => _favoriteError ?? Localization.Get(FavoriteBusy ? "FavoriteBusy" : IsFavorite ? "Unfavorite" : "Favorite");
    public double Progress => Duration.TotalSeconds > 0 ? Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds * 100, 0, 100) : 0;
    public string ElapsedText => $"{(int)Position.TotalMinutes}:{Position.Seconds:00}";
    public string DurationText => $"{(int)Duration.TotalMinutes}:{Duration.Seconds:00}";
    public string? Title { get => _title; private set { if (Set(ref _title, value)) Raise(nameof(DisplayTitle)); } }
    public string Artist { get => _artist; private set { if (Set(ref _artist, value)) Raise(nameof(DisplayTitle)); } }
    public string? Album { get => _album; private set => Set(ref _album, value); }
    public string? ArtworkUrl { get => _artworkUrl; private set => Set(ref _artworkUrl, value); }
    public TimeSpan Position { get => _position; private set => Set(ref _position, value); }
    public TimeSpan Duration { get => _duration; private set => Set(ref _duration, value); }
    public bool Playing { get => _playing; private set => Set(ref _playing, value); }
    public string DisplayTitle => string.IsNullOrEmpty(Artist) ? Title ?? "" : $"{Title} - {Artist}";
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Refresh()
    {
        if (_disposed) return;
        if (_lastSample != 0 && Stopwatch.GetElapsedTime(_lastSample).TotalMilliseconds < 100)
        {
            UpdatePositionAndLines();
            return;
        }
        _lastSample = Stopwatch.GetTimestamp();
        Localization.Refresh();
        var remote = _backend.RemoteSnapshot;
        var state = remote != null ? remote.State : _backend.GetCurrentState();
        if (state != null && (string.IsNullOrWhiteSpace(state.Title) || MediaTypeDetector.Guess(state) is MediaType.Video or MediaType.Podcast)) state = null;
        _state = state?.DeepCopy();
        Raise(nameof(CanControl));
        RefreshFavorite(state);
        // Keep frame-rate interpolation only while music is advancing.
        _timer.Interval = TimeSpan.FromMilliseconds(state?.Playing == true ? 16 : 100);
        ConnectionLabel = _backend.RemoteDeviceName is { } device ? $"{device} · {PlayerDisplayName.For(state)}" : PlayerDisplayName.For(state);
        ConnectionDetails = _backend.LastControlError ?? _backend.ServiceError ?? (remote != null ? Localization.Get("SharedPlayback") : Localization.Format("DirectPlayback", ConnectionLabel));
        if (state == null)
        {
            _hasTrack = false;
            _requestedTrack = null;
            _clock.Update("", TimeSpan.Zero, false);
            Title = null; Artist = ""; Album = null; ArtworkUrl = null;
            Position = Duration = TimeSpan.Zero; Playing = false;
            SetLyrics(null);
            var message = _backend.ServiceError ?? Localization.Get("NoSongPlaying");
            if (_noSongLine.Text != message) _noSongLine = new(TimeSpan.Zero, message, null);
            CurrentLine = _noSongLine;
            SecondaryLine = PreviousLine = EarlierLine = LaterLine = Empty;
            Translation = null; LineEnd = TimeSpan.Zero;
            Raise(nameof(Progress)); Raise(nameof(ElapsedText)); Raise(nameof(DurationText));
            return;
        }

        _hasTrack = true;
        var artist = string.Join(", ", state.Artists);
        var key = $"{(remote != null ? "shared" : "local")}|{state.SourceApp}|{state.Title}|{artist}|{state.Album}";
        if (_requestedTrack != key)
        {
            _requestedTrack = key;
        }
        if (remote == null) _ = _lyrics.UpdateAsync(state, karaoke: true);
        _clock.Update(key, state.Position, state.Playing);
        Title = state.Title; Artist = artist; Album = state.Album; ArtworkUrl = state.ArtworkUrl;
        Duration = state.Duration; Playing = state.Playing;
        if (_defaultLine.Text != DisplayTitle) _defaultLine = new LyricsLine(TimeSpan.Zero, DisplayTitle, null);

        // A late response for the previous song must never appear on this song.
        SetLyrics(remote != null ? remote.Lyrics : _lyrics.Capture(state).Lines);
        UpdatePositionAndLines();
    }

    private void UpdatePositionAndLines()
    {
        if (!_hasTrack) return;
        Position = Duration > TimeSpan.Zero && _clock.Position > Duration ? Duration : _clock.Position;
        Raise(nameof(Progress)); Raise(nameof(ElapsedText)); Raise(nameof(DurationText));
        Raise(nameof(Approximate)); Raise(nameof(TimingLabel));
        Raise(nameof(ShowTranslations));
        if (_shownLyrics == null || _shownLyrics.Count == 0)
        {
            CurrentLine = _defaultLine;
            SecondaryLine = PreviousLine = EarlierLine = LaterLine = Empty;
            Translation = null; LineEnd = TimeSpan.Zero;
            return;
        }
        var frame = KaraokeTimeline.At(_shownLyrics, Position);
        CurrentLine = frame.Current ?? Empty;
        SecondaryLine = frame.Next ?? Empty;
        var index = frame.Current == null ? -1 : Lines.IndexOf(frame.Current);
        PreviousLine = index >= 1 ? Lines[index - 1] : Empty;
        EarlierLine = index >= 2 ? Lines[index - 2] : Empty;
        LaterLine = index + 2 < Lines.Count ? Lines[index + 2] : Empty;
        LineEnd = CurrentLine.EndTime > CurrentLine.Timestamp ? CurrentLine.EndTime.Value
            : frame.Next?.Timestamp ?? Duration;
        Translation = AppearancePreferences.Current.ShowTranslation ? CurrentLine.Translation : null;
    }

    private void RefreshFavorite(PlayerState? state)
    {
        var key = state == null ? null : LyricsCache.TrackKey(state);
        if (key != _favoriteKey)
        {
            _favoriteKey = key; _nextFavoriteRead = 0; _favoriteError = null;
            SetFavorite(null);
        }
        if (state == null || _favoriteReading || FavoriteBusy || Environment.TickCount64 < _nextFavoriteRead) return;
        _nextFavoriteRead = Environment.TickCount64 + 5000;
        _ = ReadFavoriteAsync(state.DeepCopy(), key!);
    }

    private async Task ReadFavoriteAsync(PlayerState state, string key)
    {
        _favoriteReading = true;
        try
        {
            var favorite = await _backend.GetFavoriteAsync(state, _cts.Token);
            if (!_disposed && key == _favoriteKey && !FavoriteBusy)
            {
                if (favorite != null) { _favoriteError = null; Raise(nameof(FavoriteLabel)); }
                SetFavorite(favorite);
            }
        }
        finally { _favoriteReading = false; }
    }

    private void SetFavorite(FavoriteState? state)
    {
        if (_favorite == state) return;
        _favorite = state;
        Raise(nameof(FavoriteAvailable)); Raise(nameof(IsFavorite)); Raise(nameof(FavoriteEnabled)); Raise(nameof(FavoriteLabel));
    }

    public async Task ToggleFavoriteAsync()
    {
        if (!FavoriteEnabled || _state == null || _favorite == null) return;
        var state = _state.DeepCopy(); var previous = _favorite; var key = _favoriteKey;
        FavoriteBusy = true; _favoriteError = null;
        try
        {
            var result = await _backend.SetFavoriteAsync(state, previous, !previous.IsFavorite, _cts.Token);
            if (!_disposed && key == _favoriteKey)
            {
                if (result != null) SetFavorite(result);
                else _favoriteError = Localization.Get("FavoriteError");
            }
        }
        finally { FavoriteBusy = false; _nextFavoriteRead = 0; Raise(nameof(FavoriteLabel)); }
    }

    private void SetLyrics(IReadOnlyList<LyricsLine>? lines)
    {
        if (ReferenceEquals(_shownLyrics, lines)) return;
        _shownLyrics = lines;
        Lines.Clear();
        if (lines != null) foreach (var line in lines) Lines.Add(line);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _cts.Cancel();
        _prefetch.Dispose();
        _backend.Dispose();
        _cts.Dispose();
    }
}
