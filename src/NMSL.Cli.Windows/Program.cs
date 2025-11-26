using NMSL.Backends.Windows;
using NMSL.Core.Lyrics.Models;

internal class Program
{
    // Global lock to prevent concurrent console writes
    static readonly SemaphoreSlim ConsoleLock = new(1, 1);

    private static async Task Main(string[] args)
    {
        // Cancellation token
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        // Backend + lyrics service
        var backend = new SMTCBackend();
        var lyricsService = new LyricsService();

        // Playback state tracking
        List<LyricsLine>? currentLyrics = null;
        string lastSongId = "";
        int lastCenterIndex = -999;

        // Header + lyrics layout
        const int HEADER_LINES = 3;
        const int LYRICS_BUFFER = 6;

        // Where lyrics block begins
        int lyricsStartLine = HEADER_LINES;

        // ------------------ SMTC event ------------------
        backend.OnStateChanged += async (_, state) =>
        {
            if (state is null || !state.Playing)
                return;

            // Build unique ID to detect song change
            string songId = $"{state.Title}|{state.Artists.FirstOrDefault()}|{state.Duration}";

            if (songId != lastSongId)
            {
                lastSongId = songId;
                lastCenterIndex = -999;
                currentLyrics = null;

                string artists = state.Artists.Count > 0
                    ? string.Join(", ", state.Artists)
                    : "Unknown Artist";

                // Draw header immediately
                await WriteHeader(
                    "Now Playing:",
                    $"{artists} - {state.Title}",
                    "Searching lyrics..."
                );

                var parsed = await lyricsService.SearchLyricsAsync(state);

                if (parsed != null)
                {
                    currentLyrics = parsed;

                    await WriteHeader(
                        "Now Playing:",
                        $"{artists} - {state.Title}",
                        ""
                    );
                }
                else
                {
                    await WriteHeader(
                        "Now Playing:",
                        $"{artists} - {state.Title}",
                        "(No lyrics found)"
                    );
                }
            }
        };


        // ------------------ Start backend ------------------
        await backend.StartAsync(cts.Token);


        // ------------------ Main rendering loop ------------------
        while (!cts.IsCancellationRequested)
        {
            var state = backend.GetCurrentState();

            if (state != null && currentLyrics != null)
            {
                TimeSpan pos = state.Position;

                // Current center line
                int centerIdx = currentLyrics.FindLastIndex(l => l.Timestamp <= pos);

                if (centerIdx != lastCenterIndex && centerIdx >= 0)
                {
                    lastCenterIndex = centerIdx;

                    // 6-line window
                    int start = Math.Max(0, centerIdx - 3);
                    int end = Math.Min(currentLyrics.Count - 1, start + LYRICS_BUFFER - 1);

                    start = Math.Max(0, end - (LYRICS_BUFFER - 1));

                    var lines = new List<string>();
                    for (int i = start; i <= end; i++)
                    {
                        string text = currentLyrics[i].Text;
                        if (i == centerIdx)
                            lines.Add($">> {text}");
                        else
                            lines.Add($"   {text}");
                    }

                    await WriteLyricsBlock(lines, lyricsStartLine);
                }
            }

            await Task.Delay(10);
        }



        // ========================================================
        // ================   Console Helpers   ===================
        // ========================================================

        /// <summary>
        /// Writes the 3-line fixed header at top of console.
        /// Thread-safe.
        /// </summary>
        static async Task WriteHeader(string line1, string line2, string line3)
        {
            await ConsoleLock.WaitAsync();
            try
            {
                Console.SetCursorPosition(0, 0);

                string[] lines = { line1, line2, line3 };

                foreach (var line in lines)
                {
                    Console.Write(new string(' ', Console.WindowWidth)); // clear
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.WriteLine(line);
                }
            }
            finally
            {
                ConsoleLock.Release();
            }
        }

        /// <summary>
        /// Writes the lyrics block at a stable location.
        /// Thread-safe.
        /// </summary>
        static async Task WriteLyricsBlock(List<string> lines, int startLine)
        {
            await ConsoleLock.WaitAsync();
            try
            {
                Console.SetCursorPosition(0, startLine);

                foreach (var line in lines)
                {
                    Console.Write(new string(' ', Console.WindowWidth));
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.WriteLine(line);
                }

                // Move cursor below the lyrics block
                Console.SetCursorPosition(0, startLine + lines.Count);
            }
            finally
            {
                ConsoleLock.Release();
            }
        }
    }
}