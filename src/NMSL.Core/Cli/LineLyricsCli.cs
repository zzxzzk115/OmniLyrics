using NMSL.Core;
using NMSL.Core.Cli;

public class LineLyricsCli : BaseLyricsCli
{
    public LineLyricsCli(IPlayerBackend backend) : base(backend) { }

    protected override void RenderLyricsFrame()
    {
        var state = Backend.GetCurrentState();
        if (state is null || CurrentLyrics is null)
            return;

        var pos = state.Position;
        int idx = CurrentLyrics.FindLastIndex(l => l.Timestamp <= pos);
        if (idx < 0 || idx == LastCenterIndex)
            return;

        LastCenterIndex = idx;
        RenderSingleLine(CurrentLyrics[idx].Text);
    }

    protected override Task RedrawScreenAsync(string a, string b, string c, List<string>? d)
        => Task.CompletedTask; // Disable full redraw
}