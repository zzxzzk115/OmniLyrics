using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OmniLyrics.Backends.Dynamic;
using OmniLyrics.Core;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Shared;

namespace OmniLyrics.Gui.Models;

public class LyricsViewModel : INotifyPropertyChanged
{
    private readonly DynamicBackend _backend = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly LyricsLine _emptyLyricsLine = new(TimeSpan.Zero, string.Empty, null);
    private readonly LyricsManager _lyrics = new();

    private string? _album;

    private string _artist = "";

    private string? _artworkUrl;
    private LyricsLine _defaultLyricsLine;

    private TimeSpan _duration;

    private bool _playing;

    private TimeSpan _position;

    private string? _title;

    public LyricsViewModel()
    {
        _backend.OnStateChanged += Backend_OnStateChanged;
        Task.Run(async () =>
        {
            await _backend.StartAsync(_cts.Token);
            while (!_cts.IsCancellationRequested)
            {
                var state = _backend.GetCurrentState();
                if (state == null) continue;

                Update(state);
                await Task.Delay(16);
            }
        });
    }

    public IPlayerBackend Backend => _backend;

    public ObservableCollection<LyricsLine> Lines { get; } = new();

    public LyricsLine CurrentLine { get; private set; }
    public LyricsLine SecondaryLine { get; private set; }

    public string? Title
    {
        get => _title;
        private set
        {
            _title = value;
            Raise(nameof(Title));
            Raise(nameof(DisplayTitle));
        }
    }

    public string Artist
    {
        get => _artist;
        private set
        {
            _artist = value;
            Raise(nameof(Artist));
            Raise(nameof(DisplayTitle));
        }
    }

    public string? Album
    {
        get => _album;
        private set
        {
            _album = value;
            Raise(nameof(Album));
            Raise(nameof(DisplayTitle));
        }
    }

    public string? ArtworkUrl
    {
        get => _artworkUrl;
        private set
        {
            _artworkUrl = value;
            Raise(nameof(ArtworkUrl));
        }
    }

    public TimeSpan Position
    {
        get => _position;
        private set
        {
            _position = value;
            Raise(nameof(Position));
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        private set
        {
            _duration = value;
            Raise(nameof(Duration));
        }
    }

    public bool Playing
    {
        get => _playing;
        private set
        {
            _playing = value;
            Raise(nameof(Playing));
        }
    }

    public string DisplayTitle => $"{Title} - {Artist} - {Album}";

    public string DefaultTitle => $"{Title} - {Artist}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void Backend_OnStateChanged(object? _, PlayerState state)
    {
        await _lyrics.UpdateAsync(state, true);

        Update(state);
    }

    private void Update(PlayerState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Title = state.Title;
            Artist = string.Join(", ", state.Artists);
            Album = state.Album;
            Position = state.Position;
            Duration = state.Duration;
            ArtworkUrl = state.ArtworkUrl;
            Playing = state.Playing;

            UpdateLines();
            UpdateCurrentByTime();

            _defaultLyricsLine = new LyricsLine(TimeSpan.Zero, DefaultTitle, null);
        });
    }

    private void UpdateLines()
    {
        Lines.Clear();
        if (_lyrics.Current != null)
        {
            foreach (var line in _lyrics.Current)
                Lines.Add(line);
        }
    }

    private void UpdateCurrentByTime()
    {
        if (_lyrics.Current == null || _lyrics.Current.Count == 0)
        {
            CurrentLine = _defaultLyricsLine;
            Raise(nameof(CurrentLine));

            SecondaryLine = _emptyLyricsLine;
            Raise(nameof(SecondaryLine));
            return;
        }

        var pos = Position;

        var current = _lyrics.Current
            .Where(l => l.Timestamp <= pos)
            .OrderBy(l => l.Timestamp)
            .LastOrDefault();

        if (current == null)
            return;

        CurrentLine = current;
        Raise(nameof(CurrentLine));

        int index = _lyrics.Current.IndexOf(current);
        if (index + 1 < _lyrics.Current.Count)
            SecondaryLine = _lyrics.Current[index + 1];
        else
            SecondaryLine = _emptyLyricsLine;

        Raise(nameof(SecondaryLine));
    }

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}