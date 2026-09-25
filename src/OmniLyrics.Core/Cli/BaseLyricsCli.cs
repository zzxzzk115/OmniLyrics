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
    protected readonly LyricsManager LyricsManager;
    public LyricsManager SharedLyricsManager => LyricsManager;

    protected int LastCenterIndex = -999;
    protected long LastLyricsRevision = -1;
    protected string LastSongId = "";

    protected BaseLyricsCli(IPlayerBackend backend, LyricsManager? lyrics = null)
    {
        Backend = backend;
        LyricsManager = lyrics ?? (backend is SharedPlayerSession session ? session.Lyrics : new());

    }

    protected (List<LyricsLine>? Lines, bool Loading, long Revision) CaptureLyrics(PlayerState? state) =>
        Backend is SharedPlayerSession session ? session.CaptureLyrics(state) : LyricsManager.Capture(state);

    List<LyricsLine>? ILyricsProvider.CurrentLyrics => CaptureLyrics(Backend.GetCurrentState()).Lines;

    /// <summary>
    ///     Start backend and refresh UI repeatedly.
    /// </summary>
    public async Task RunAsync(CancellationToken token)
    {
        await Backend.StartAsync(token);
        using var prefetch = new LyricsPrefetcher(Backend, LyricsManager, token, () => Backend is not SharedPlayerSession);
        long nextUpdate = 0;

        while (!token.IsCancellationRequested)
        {
            if (Environment.TickCount64 >= nextUpdate)
            {
                nextUpdate = Environment.TickCount64 + 250;
                Localization.Refresh();
                if (Backend is not SharedPlayerSession) _ = LyricsManager.UpdateAsync(Backend.GetCurrentState(), true);
            }
            RenderLyricsFrame();
            await Task.Delay(10, token);
        }
    }

    private string? _emptyFrame;
    protected bool RenderEmptyFrame(PlayerState? state, (List<LyricsLine>? Lines, bool Loading, long Revision) snapshot, bool singleLine = false)
    {
        if (snapshot.Lines is { Count: > 0 }) { _emptyFrame = null; return false; }
        var title = state == null ? Localization.Get("Waiting") : $"{string.Join(", ", state.Artists)} - {state.Title}";
        var message = state == null ? "" : Localization.Get(snapshot.Loading ? "SearchingLyrics" : "NoLyrics");
        var key = title + "|" + message;
        if (_emptyFrame == key) return true;
        _emptyFrame = key; LastCenterIndex = -999; LastLyricsRevision = -1;
        if (singleLine) RenderSingleLine(title + (message.Length > 0 ? " · " + message : ""));
        else _ = RedrawScreenAsync(Localization.Text("Now Playing:"), title, message, null);
        return true;
    }

    /// <summary>
    ///     Main lyric rendering pass (called frequently)
    /// </summary>
    protected virtual void RenderLyricsFrame()
    {
        var state = Backend.GetCurrentState();
        var snapshot = CaptureLyrics(state);
        var cur = snapshot.Lines;
        if (RenderEmptyFrame(state, snapshot) || state == null || cur == null) return;

        int idx = cur.FindLastIndex(l => l.Timestamp <= state.Position);

        var track = LyricsCache.TrackKey(state);
        if (idx == LastCenterIndex && snapshot.Revision == LastLyricsRevision && LastSongId == track)
            return;

        LastSongId = track;
        LastCenterIndex = idx;
        LastLyricsRevision = snapshot.Revision;

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
            : Localization.Text("Unknown Artist");

        _ = RedrawScreenAsync(
            Localization.Text("Now Playing:"),
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
