using NMSL.Core;
using NMSL.Core.Lyrics.Models;

namespace NMSL.Core.Cli;

/// <summary>
/// Base class for building a CLI lyrics display, independent of backend type.
/// This version is fully cross-platform and uses only Console.Clear() + WriteLine(),
/// avoiding cursor operations that fail under Linux/WSL.
/// </summary>
public abstract class BaseLyricsCli
{
    // Protects console redraw from concurrent access
    protected static readonly SemaphoreSlim ConsoleLock = new(1, 1);

    // Prevents concurrent song-change logic
    protected static readonly SemaphoreSlim SongChangeLock = new(1, 1);

    protected readonly IPlayerBackend Backend;
    protected readonly LyricsService LyricsService;

    // Lyrics + playback tracking
    protected List<LyricsLine>? CurrentLyrics = null;
    protected string LastSongId = "";
    protected int LastCenterIndex = -999;

    protected BaseLyricsCli(IPlayerBackend backend)
    {
        Backend = backend;
        LyricsService = new LyricsService();

        // Subscribe to backend updates
        Backend.OnStateChanged += async (_, state) =>
        {
            await HandleBackendStateChangedAsync(state);
        };
    }

    /// <summary>
    /// Start backend + main render loop.
    /// </summary>
    public async Task RunAsync(CancellationToken token)
    {
        // Start backend (SMTC/MPRIS/etc)
        await Backend.StartAsync(token);

        // Main loop
        while (!token.IsCancellationRequested)
        {
            RenderLyricsFrame();
            await Task.Delay(10, token);
        }
    }

    /// <summary>
    /// Fired when backend reports playback state changed.
    /// Handles detecting new song + loading lyrics.
    /// </summary>
    private async Task HandleBackendStateChangedAsync(PlayerState? state)
    {
        if (state is null || !state.Playing)
            return;

        // Normalize identifiers for change detection
        string normTitle = (state.Title ?? "").Trim().ToLowerInvariant();
        string normArtist = (state.Artists.FirstOrDefault() ?? "").Trim().ToLowerInvariant();
        string songId = $"{normTitle}|{normArtist}";

        bool shouldLoadLyrics = false;
        string artistsText = "";

        await SongChangeLock.WaitAsync();
        try
        {
            // Same song? Ignore.
            if (songId == LastSongId)
                return;

            // Mark this as new song
            LastSongId = songId;
            LastCenterIndex = -999;
            CurrentLyrics = null;

            artistsText = state.Artists.Count > 0
                ? string.Join(", ", state.Artists)
                : "Unknown Artist";

            shouldLoadLyrics = true;
        }
        finally
        {
            SongChangeLock.Release();
        }

        if (!shouldLoadLyrics)
            return;

        // Immediately show header (Searching...)
        await RedrawScreenAsync(
            "Now Playing:",
            $"{artistsText} - {state.Title}",
            "Searching lyrics...",
            null
        );

        // Load lyrics
        var parsed = await LyricsService.SearchLyricsAsync(state);

        if (parsed != null)
        {
            CurrentLyrics = parsed;

            await RedrawScreenAsync(
                "Now Playing:",
                $"{artistsText} - {state.Title}",
                "",
                null
            );
        }
        else
        {
            await RedrawScreenAsync(
                "Now Playing:",
                $"{artistsText} - {state.Title}",
                "(No lyrics found)",
                null
            );
        }
    }


    // ====================================================================
    // Rendering (virtual for subclass override)
    // ====================================================================

    /// <summary>
    /// Redraw lyrics window (called frequently).
    /// Virtual so subclasses can implement custom rendering styles.
    /// </summary>
    protected virtual void RenderLyricsFrame()
    {
        var state = Backend.GetCurrentState();
        if (state is null || CurrentLyrics is null)
            return;

        TimeSpan pos = state.Position;

        // Find current lyric line
        int centerIdx = CurrentLyrics.FindLastIndex(l => l.Timestamp <= pos);
        if (centerIdx < 0 || centerIdx == LastCenterIndex)
            return;

        LastCenterIndex = centerIdx;

        // Make lyrics block (fixed window: 6 lines)
        const int BUFFER = 6;

        int start = Math.Max(0, centerIdx - 3);
        int end = Math.Min(CurrentLyrics.Count - 1, start + BUFFER - 1);
        start = Math.Max(0, end - (BUFFER - 1));

        var lines = new List<string>();
        for (int i = start; i <= end; i++)
        {
            var text = CurrentLyrics[i].Text;
            lines.Add(i == centerIdx ? $">> {text}" : $"   {text}");
        }

        // Redraw full screen with updated lyrics
        var artistsText = state.Artists.Count > 0
            ? string.Join(", ", state.Artists)
            : "Unknown Artist";

        _ = RedrawScreenAsync(
            "Now Playing:",
            $"{artistsText} - {state.Title}",
            "",
            lines
        );
    }

    /// <summary>
    /// Fully clears screen & prints header + lyrics block.
    /// Safe for all terminals (Windows/Linux/WSL), no cursor movement.
    /// Virtual so subclasses may replace full redraw with single-line output.
    /// </summary>
    protected virtual async Task RedrawScreenAsync(
        string line1,
        string line2,
        string line3,
        List<string>? lyrics)
    {
        await ConsoleLock.WaitAsync();
        try
        {
            Console.Clear();

            Console.WriteLine(line1);
            Console.WriteLine(line2);
            Console.WriteLine(line3);
            Console.WriteLine();

            if (lyrics != null)
            {
                foreach (var l in lyrics)
                    Console.WriteLine(l);
            }
        }
        finally
        {
            ConsoleLock.Release();
        }
    }

    /// <summary>
    /// Outputs a single line. Can be used by lightweight CLI modes 
    /// (such as status bar integration).
    /// </summary>
    protected virtual void RenderSingleLine(string text)
    {
        Console.WriteLine(text);
        Console.Out.Flush();
    }
}