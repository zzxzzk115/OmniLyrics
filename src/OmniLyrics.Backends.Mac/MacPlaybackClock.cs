namespace OmniLyrics.Backends.Mac;

/// <summary>Continuously follow coarse Apple Events reports without stepping at each poll.</summary>
internal sealed class MacPlaybackClock(TimeProvider time)
{
    private double _anchor, _reported, _rate = 1;
    private long _anchorTime, _movingReportTime;
    private bool _playing, _initialized;

    public TimeSpan Position
    {
        get
        {
            if (!_playing) return TimeSpan.FromSeconds(_anchor);
            // A player that stops advancing its reports must not run lyrics forever.
            var now = time.GetTimestamp();
            var movingAge = time.GetElapsedTime(_movingReportTime, now).TotalSeconds;
            var elapsed = time.GetElapsedTime(_anchorTime, now).TotalSeconds;
            elapsed = Math.Max(0, elapsed - Math.Max(0, movingAge - 2));
            return TimeSpan.FromSeconds(_anchor + elapsed * _rate);
        }
    }

    public void Update(TimeSpan position, bool playing, bool reset = false)
    {
        var now = time.GetTimestamp();
        var seconds = Math.Max(0, position.TotalSeconds);
        var current = Position.TotalSeconds;
        var changed = seconds != _reported;
        // A second of coarse position data plus IPC latency is normal. Large
        // discontinuities and explicit seek commands are transport changes.
        var discontinuity = changed && (seconds < _reported - .25 || Math.Abs(seconds - current) > 2);
        if (!_initialized || reset || playing != _playing || discontinuity || !playing)
        {
            _anchor = seconds; _anchorTime = _movingReportTime = now; _rate = 1;
        }
        else if (changed)
        {
            _anchor = current; _anchorTime = _movingReportTime = now;
            // Keep position continuous. Correct a small offset through clock speed,
            // at no more than five percent, rather than jumping the karaoke cursor.
            _rate = 1 + Math.Clamp((seconds - current) / 8, -.05, .05);
        }
        _initialized = true; _playing = playing; _reported = seconds;
    }
}
