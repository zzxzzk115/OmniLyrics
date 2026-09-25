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
        var snapshot = CaptureLyrics(state);
        var lyrics = snapshot.Lines;

        if (RenderEmptyFrame(state, snapshot, singleLine: true) || state == null || lyrics == null) return;

        var pos = state.Position;
        int idx = lyrics.FindLastIndex(l => l.Timestamp <= pos);
        var track = OmniLyrics.Core.Shared.LyricsCache.TrackKey(state);
        if (idx == LastCenterIndex && snapshot.Revision == LastLyricsRevision && LastSongId == track)
            return;

        LastSongId = track;
        LastCenterIndex = idx;
        LastLyricsRevision = snapshot.Revision;
        RenderSingleLine(idx >= 0 ? lyrics[idx].Text : $"{string.Join(", ", state.Artists)} - {state.Title}");
    }

    // Disable full redraw path entirely
    protected override Task RedrawScreenAsync(string a, string b, string c, List<string>? d)
        => Task.CompletedTask;
}
