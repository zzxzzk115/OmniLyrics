using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using OmniLyrics.Core.Lyrics;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Gui.Controls;

var passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    passed++;
}
TimeSpan Ms(double n) => TimeSpan.FromMilliseconds(n);

var parsed = LyricsService.ParseLyrics("[1000,2000](1000,800,0)Hello (1800,1200,0)world", LyricsRawTypes.Yrc)!;
Check("YRC retains syllable starts and durations, not absolute ends",
    parsed.Count == 1 && parsed[0].Tokens is { Count: 2 } tokens
    && tokens[0].StartTime == Ms(1000) && tokens[0].Duration == Ms(800)
    && tokens[1].StartTime == Ms(1800) && tokens[1].Duration == Ms(1200));
var qrc = LyricsService.ParseLyrics("<QrcInfos><LyricInfo LyricCount=\"1\"><Lyric_1 LyricType=\"1\" LyricContent=\"[1000,2000]Hello (1000,800)world(1800,1200)\"/></LyricInfo></QrcInfos>", LyricsRawTypes.Qrc);
Check("QRC retains genuine per-word timing", qrc is { Count: 1 } && qrc[0].Tokens is { Count: 2 } qrcTokens
    && qrcTokens[1].StartTime == Ms(1800) && qrcTokens[1].Duration == Ms(1200));
var lrc = LyricsService.ParseLyrics("[00:03.00]Last line\n[00:01.00]First &amp; next", LyricsRawTypes.Lrc)!;
Check("LRC stays untimed per word, decodes entities and sorts lines",
    lrc[0].Text == "First & next" && lrc.All(l => l.Tokens == null) && lrc[1].Timestamp == Ms(3000));
var line = new LyricsLine(Ms(1000), "Go, go!", [new(Ms(1000), Ms(800), "Go"), new(Ms(2000), Ms(600), "go")]);
var ranges = KaraokeTimeline.GetRanges(line);
Check("Tokens map to shaped text without losing punctuation", ranges[0].Start == 0 && ranges[1].Start == 4);
Check("Highlight waits for its syllable", ranges[0].ProgressAt(Ms(900)) == 0);
Check("Highlight follows actual duration", ranges[0].ProgressAt(Ms(1400)) == .5);
Check("Highlight clamps completed syllables", ranges[0].ProgressAt(Ms(2000)) == 1);
Check("Zero-duration syllables do not divide by zero", new TimedTextRange(0, 1, Ms(1000), Ms(0)).ProgressAt(Ms(1000)) == 1);
Check("Before first line only the preview is shown", KaraokeTimeline.At(lrc, Ms(0)) == (null, lrc[0]));
Check("Line switch happens at its timestamp", KaraokeTimeline.At(lrc, Ms(3000)) == (lrc[1], null));
Check("Backward seek restores previous line", KaraokeTimeline.At(lrc, Ms(1500)) == (lrc[0], lrc[1]));
Check("Empty lyrics are safe", KaraokeTimeline.At([], Ms(1000)) == (null, null));

var time = new ManualTime();
var clock = new PlaybackClock(time);
clock.Update("a", Ms(1000), true); time.Advance(100);
Check("Playing position interpolates monotonically", clock.Position == Ms(1100));
clock.Update("a", Ms(1000), true); time.Advance(100);
Check("Repeated polling does not restart interpolation", clock.Position == Ms(1200));
clock.Update("a", Ms(1200), false); time.Advance(300);
Check("Pause freezes highlight", clock.Position == Ms(1200));
clock.Update("a", Ms(1200), true); time.Advance(100);
Check("Resume continues from paused position", clock.Position == Ms(1300));
clock.Update("a", Ms(500), true);
Check("Backward seek snaps to player position", clock.Position == Ms(500));
clock.Update("a", Ms(9000), true);
Check("Forward seek snaps to player position", clock.Position == Ms(9000));
clock.Update("b", Ms(0), true);
Check("Track changes reset the clock", clock.Position == TimeSpan.Zero);
time.Advance(10000);
Check("Missing player updates cannot run lyrics far ahead", clock.Position == Ms(2000));

// YesPlayMusic reports a new position about once a second. Polling that value
// faster must not produce a half-second stall or rewind on each report.
time = new ManualTime(); clock = new PlaybackClock(time);
clock.Update("coarse", Ms(0), true);
var previous = clock.Position;
var smooth = true;
for (var frame = 1; frame <= 750; frame++)
{
    time.Advance(16);
    // Model 100 ms client polls, with a one-second player sample and jitter.
    if (frame % 7 == 0)
    {
        var reported = Math.Floor(frame * 16 / 1000d) * 1000;
        clock.Update("coarse", Ms(reported), true);
    }
    var step = (clock.Position - previous).TotalMilliseconds;
    smooth &= step >= 15 && step <= 17;
    previous = clock.Position;
}
Check("One-second player reports advance smoothly on every rendered frame", smooth);
Check("Polling jitter stays close to the playback timeline", Math.Abs(clock.Position.TotalMilliseconds - 12000) < 150);
var beforeCorrection = clock.Position;
var correction = clock.Position - Ms(100);
clock.Update("coarse", correction, true);
Check("Small timing corrections never jump the highlight backward", clock.Position == beforeCorrection);
time.Advance(16);
Check("Gradual correction keeps the highlight moving", clock.Position > beforeCorrection);
time.Advance(5000);
var stalled = clock.Position;
clock.Update("coarse", correction, true);
time.Advance(5000);
Check("Repeated stale polls cannot renew the interpolation budget", clock.Position == stalled);
clock.Update("coarse", Ms(1000), true);
Check("A backward seek still resets a stalled timeline", clock.Position == Ms(1000));
clock.Update("coarse", Ms(50000), true);
Check("A forward seek still resets a stalled timeline", clock.Position == Ms(50000));

// Use the production Avalonia renderer. No real player or library is accessed.
AppBuilder.Configure<Application>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
var control = new KaraokeLine { Line = line, FontSize = 36, Width = 600, Height = 72 };
int GoldPixels(double position)
{
    control.Position = Ms(position);
    control.Measure(new Size(600, 72));
    control.Arrange(new Rect(0, 0, 600, 72));
    using var bitmap = new RenderTargetBitmap(new PixelSize(600, 72));
    bitmap.Render(control);
    using var pixels = new WriteableBitmap(new PixelSize(600, 72), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
    using var fb = pixels.Lock();
    bitmap.CopyPixels(fb, AlphaFormat.Premul);
    var data = new byte[fb.RowBytes * fb.Size.Height];
    Marshal.Copy(fb.Address, data, 0, data.Length);
    var count = 0;
    for (var i = 0; i < data.Length; i += 4)
        if (data[i+3] > 100 && data[i+2] > data[i] + 35 && data[i+1] > data[i] + 20) count++;
    return count;
}
var before = GoldPixels(500); var halfway = GoldPixels(1400); var complete = GoldPixels(3000);
Check("Rendered gold pixels advance with actual syllable time", before == 0 && halfway > 0 && complete > halfway);
Check("Rendered backward seek removes future highlight", GoldPixels(500) == 0);
control.Line = new LyricsLine(Ms(0), line.Text, null);
Check("Ordinary LRC renders a fully highlighted line", GoldPixels(500) >= complete);
control.Approximate = true; control.LineEnd = Ms(4000);
Check("Optional simulation starts unhighlighted and progresses", GoldPixels(0) == 0 && GoldPixels(2000) > 0 && GoldPixels(4000) > GoldPixels(2000));
Check("Simulation never creates source word timings", control.Line.Tokens == null);
control.Approximate = false;
control.Line = new LyricsLine(Ms(0), "星光陪我们慢慢唱", [new(Ms(0), Ms(1000), "星光"), new(Ms(1000), Ms(1500), "陪我们"), new(Ms(2500), Ms(1500), "慢慢唱")]);
Check("Chinese syllables render and highlight", GoldPixels(1500) > 0);
control.Line = new LyricsLine(Ms(0), new string('W', 120), null);
Check("Long lines fit without renderer errors", GoldPixels(1000) > 0);

control.Wrap = true; control.Width = 220; control.Height = double.NaN;
control.Measure(new Size(220, double.PositiveInfinity));
Check("Reading presets wrap long lyrics across multiple lines", control.DesiredSize.Height > 72);
Check("Simulated progress clamps before and after a line", KaraokeTimeline.ApproximateProgress(line, Ms(3000), Ms(0)) == 0 && KaraokeTimeline.ApproximateProgress(line, Ms(3000), Ms(5000)) == 1);

if (args.Length == 2 && args[0] == "--preview")
{
    var sample = new LyricsLine(Ms(0), "星光陪我们慢慢唱", [new(Ms(0), Ms(800), "星光"), new(Ms(800), Ms(900), "陪我们"), new(Ms(1700), Ms(1400), "慢慢唱")]);
    var panel = new StackPanel { Spacing = 16, Margin = new Thickness(32) };
    panel.Children.Add(new TextBlock { Text = "OmniLyrics · Karaoke", FontSize = 18, Foreground = Brushes.White });
    foreach (var p in new[] { 1150, 2400 })
    {
        panel.Children.Add(new KaraokeLine { Line = sample, Position = Ms(p), FontSize = 36, Height = 54 });
        panel.Children.Add(new TextBlock { Text = p == 1150 ? "00:01.15" : "00:02.40", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Foreground = Brushes.Gray });
    }
    var card = new Border { Background = new SolidColorBrush(Color.Parse("#181D27")), Child = panel };
    card.Measure(new Size(760, 300)); card.Arrange(new Rect(0, 0, 760, 300));
    using var bitmap = new RenderTargetBitmap(new PixelSize(760, 300));
    bitmap.Render(card); bitmap.Save(args[1]);
}
Console.WriteLine($"{passed} checks passed");

sealed class ManualTime : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => _ticks;
    public void Advance(long milliseconds) => _ticks += milliseconds;
}
