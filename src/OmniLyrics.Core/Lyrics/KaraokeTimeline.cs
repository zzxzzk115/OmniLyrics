using OmniLyrics.Core.Lyrics.Models;

namespace OmniLyrics.Core.Lyrics;

public sealed record TimedTextRange(int Start, int Length, TimeSpan StartTime, TimeSpan Duration)
{
    public double ProgressAt(TimeSpan position) => Duration <= TimeSpan.Zero
        ? (position >= StartTime ? 1 : 0)
        : Math.Clamp((position - StartTime).TotalSeconds / Duration.TotalSeconds, 0, 1);
}

public static class KaraokeTimeline
{
    // Presentation-only estimate. Never stored as syllable timings or sent to clients.
    public static double ApproximateProgress(LyricsLine line, TimeSpan end, TimeSpan position) => end <= line.Timestamp
        ? 1 : Math.Clamp((position - line.Timestamp).TotalMilliseconds / (end - line.Timestamp).TotalMilliseconds, 0, 1);

    public static IReadOnlyList<TimedTextRange> GetRanges(LyricsLine? line)
    {
        var result = new List<TimedTextRange>();
        if (line?.Tokens == null || !line.Tokens.Any(token => token.Duration > TimeSpan.Zero)) return result;
        var offset = 0;
        foreach (var token in line.Tokens)
        {
            if (string.IsNullOrEmpty(token.Text)) continue;
            var start = line.Text.IndexOf(token.Text, offset, StringComparison.Ordinal);
            if (start < 0) continue;
            result.Add(new(start, token.Text.Length, token.StartTime, token.Duration));
            offset = start + token.Text.Length;
        }
        return result;
    }

    public static (LyricsLine? Current, LyricsLine? Next) At(IReadOnlyList<LyricsLine> lines, TimeSpan position)
    {
        var left = 0;
        var right = lines.Count - 1;
        while (left <= right)
        {
            var middle = left + (right - left) / 2;
            if (lines[middle].Timestamp <= position) left = middle + 1;
            else right = middle - 1;
        }
        return (right >= 0 ? lines[right] : null, left < lines.Count ? lines[left] : null);
    }
}

/// <summary>Interpolate between player reports using a monotonic clock.</summary>
public sealed class PlaybackClock(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private string? _track;
    private TimeSpan _position;
    private bool _playing;
    private long _timestamp;

    public void Update(string track, TimeSpan position, bool playing)
    {
        if (_track == track && _position == position && _playing == playing) return;
        _track = track;
        _position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        _playing = playing;
        _timestamp = _time.GetTimestamp();
    }

    public TimeSpan Position => _position + (_playing
        ? TimeSpan.FromMilliseconds(Math.Clamp(_time.GetElapsedTime(_timestamp).TotalMilliseconds, 0, 500))
        : TimeSpan.Zero);
}
