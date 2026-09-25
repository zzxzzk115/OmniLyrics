using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Shared;

namespace OmniLyrics.Core.Cli;

/// <summary>
///     Base class for CLI lyrics output (multiline / single line).
///     This version is cross-platform: Windows / macOS / Linux / WSL.
///     Relies on full redraw instead of cursor movement.
/// </summary>
public abstract class BaseLyricsCli : ILyricsProvider
{
    // Prevents interleaved console print
    protected static readonly SemaphoreSlim ConsoleLock = new(1, 1);

    protected readonly IPlayerBackend Backend;
    protected readonly LyricsManager LyricsManager = new();

    protected int LastCenterIndex = -999;
    protected string LastSongId = "";

    protected BaseLyricsCli(IPlayerBackend backend)
    {
        Backend = backend;

        // Receive backend events (async lyrics load)
        Backend.OnStateChanged += async (_, state) =>
        {
            await HandleBackendStateChangedAsync(state);
        };
    }

    List<LyricsLine>? ILyricsProvider.CurrentLyrics => LyricsManager.Current;

    /// <summary>
    ///     Start backend and refresh UI repeatedly.
    /// </summary>
    public async Task RunAsync(CancellationToken token)
    {
        await Backend.StartAsync(token);

        while (!token.IsCancellationRequested)
        {
            RenderLyricsFrame();
            await Task.Delay(10);
        }
    }

    /// <summary>
    ///     Handle new song detection and async lyrics load.
    /// </summary>
    private async Task HandleBackendStateChangedAsync(PlayerState? state)
    {
        if (state is null)
            return;

        string normTitle = (state.Title ?? "").Trim().ToLowerInvariant();
        string normArtist = (state.Artists.FirstOrDefault() ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(normTitle) || string.IsNullOrEmpty(normArtist))
            return;

        string songId = $"{state.SourceApp}|{normTitle}|{normArtist}";

        if (songId == LastSongId)
            return;

        LastSongId = songId;
        LastCenterIndex = -999;

        string artistText = state.Artists.Count > 0
            ? string.Join(", ", state.Artists)
            : "Unknown Artist";

        // UI: show searching placeholder
        await RedrawScreenAsync(
            "Now Playing:",
            $"{artistText} - {state.Title}",
            "Searching lyrics...",
            null
        );

        // Load from shared manager (cache-aware)
        await LyricsManager.UpdateAsync(state, false);

        var lines = LyricsManager.Current;
        if (lines == null)
        {
            await RedrawScreenAsync(
                "Now Playing:",
                $"{artistText} - {state.Title}",
                "(No lyrics found)",
                null
            );
        }
        else
        {
            await RedrawScreenAsync(
                "Now Playing:",
                $"{artistText} - {state.Title}",
                "",
                null
            );
        }
    }

    /// <summary>
    ///     Main lyric rendering pass (called frequently)
    /// </summary>
    protected virtual void RenderLyricsFrame()
    {
        var state = Backend.GetCurrentState();
        var cur = LyricsManager.Current;
        if (state == null || cur == null || cur.Count == 0)
            return;

        int idx = cur.FindLastIndex(l => l.Timestamp <= state.Position);

        if (idx < 0 || idx == LastCenterIndex)
            return;

        LastCenterIndex = idx;

        const int N = 6;
        int start = Math.Max(0, idx - 3);
        int end = Math.Min(cur.Count - 1, start + N - 1);
        start = Math.Max(0, end - (N - 1));

        var lines = new List<string>();
        for (int i = start; i <= end; i++)
        {
            string t = cur[i].Text;
            lines.Add(i == idx ? $">> {t}" : $"   {t}");
        }

        string artistText = state.Artists.Count > 0
            ? string.Join(", ", state.Artists)
            : "Unknown Artist";

        _ = RedrawScreenAsync(
            "Now Playing:",
            $"{artistText} - {state.Title}",
            "",
            lines
        );
    }

    /// <summary>
    ///     Full redraw (page clear + header + lyrics)
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
                foreach (string l in lyrics)
                    Console.WriteLine(l);
            }
        }
        finally
        {
            ConsoleLock.Release();
        }
    }

    /// <summary>
    ///     Lightweight one-line output (Waybar, i3status, etc.)
    /// </summary>
    protected virtual void RenderSingleLine(string text)
    {
        Console.Clear();
        Console.WriteLine(text);
        Console.Out.Flush();
    }
}