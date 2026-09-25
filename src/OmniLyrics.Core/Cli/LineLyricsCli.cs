using OmniLyrics.Core;
using OmniLyrics.Core.Cli;

public class LineLyricsCli : BaseLyricsCli
{
    public LineLyricsCli(IPlayerBackend backend)
        : base(backend)
    {
    }

    protected override void RenderLyricsFrame()
    {
        var state = Backend.GetCurrentState();
        var lyrics = LyricsManager.Current;

        if (state is null || lyrics is null || lyrics.Count == 0)
            return;

        var pos = state.Position;
        int idx = lyrics.FindLastIndex(l => l.Timestamp <= pos);
        if (idx < 0 || idx == LastCenterIndex)
            return;

        LastCenterIndex = idx;
        RenderSingleLine(lyrics[idx].Text);
    }

    // Disable full redraw path entirely
    protected override Task RedrawScreenAsync(string a, string b, string c, List<string>? d)
        => Task.CompletedTask;
}