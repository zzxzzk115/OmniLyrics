using System.Diagnostics;
using System.Text.Json;
using OmniLyrics.Backends.Dynamic;
using OmniLyrics.Backends.Mac;
using OmniLyrics.Core;

int checks = 0;
void Check(string name, bool value)
{
    if (!value) throw new Exception(name);
    Console.WriteLine("PASS " + name); checks++;
}
async Task Until(Func<bool> predicate, int milliseconds = 5000)
{
    var watch = Stopwatch.StartNew();
    while (watch.ElapsedMilliseconds < milliseconds) { if (predicate()) return; await Task.Delay(25); }
    throw new Exception("Condition timed out");
}
PlayerState Song(string source, bool playing = true) => new()
{ SourceApp = source, Title = source + " song", Artists = ["Artist"], Album = "Album", Duration = TimeSpan.FromMinutes(3), Playing = playing };

if (args.Contains("--live-apple-music"))
{
    if (!OperatingSystem.IsMacOS()) throw new Exception("Live test requires macOS and a selected Apple Music song.");
    Check("Live setup probe agrees with authorized Apple Music", (await MacEnvironment.ProbeAsync()).NativeAvailable == true);
    var originalSetting = Environment.GetEnvironmentVariable("OMNILYRICS_MEDIA_CONTROL");
    try
    {
        foreach (var fallback in new[] { "off", "auto" })
        {
            Environment.SetEnvironmentVariable("OMNILYRICS_MEDIA_CONTROL", fallback == "off" ? "off" : null);
            using var backend = new DynamicBackend();
            await backend.StartAsync(default);
            await Until(() => backend.GetCurrentState() is { SourceApp: MacOSAppleEventsBackend.MusicBundle, PlayerName: "Apple Music" }, 15000);
            var initial = backend.GetCurrentState()!;
            Check($"{fallback}: native Apple Music metadata wins", initial.PlayerName == "Apple Music" && initial.Duration.TotalSeconds > 0);
            try
            {
                await backend.PlayAsync(); await Until(() => backend.GetCurrentState()?.Playing == true);
                var first = backend.GetCurrentState()!.Position;
                await Until(() => backend.GetCurrentState()!.Position > first + TimeSpan.FromSeconds(.6));
                Check($"{fallback}: native position advances", true);
                var lastPosition = backend.GetCurrentState()!.Position; bool monotonic = true, nativeOnly = true, continuous = true;
                var lastTick = Stopwatch.GetTimestamp(); double largestCorrection = 0;
                for (int i = 0; i < 200; i++)
                {
                    await Task.Delay(50);
                    var sample = backend.GetCurrentState();
                    nativeOnly &= sample is { PlayerName: "Apple Music" } && sample.Title == initial.Title;
                    var tick = Stopwatch.GetTimestamp(); var elapsed = Stopwatch.GetElapsedTime(lastTick, tick).TotalSeconds;
                    if (sample != null)
                    {
                        var step = (sample.Position - lastPosition).TotalSeconds;
                        monotonic &= step >= 0;
                        largestCorrection = Math.Max(largestCorrection, Math.Abs(step - elapsed));
                        continuous &= Math.Abs(step - elapsed) <= elapsed * .06 + .02;
                        lastPosition = sample.Position;
                    }
                    lastTick = tick;
                }
                Check($"{fallback}: 200 live samples keep native selection and never rewind", nativeOnly && monotonic);
                Check($"{fallback}: live karaoke advances continuously without forward steps", continuous);
                Console.WriteLine($"{fallback}: largest sample correction {largestCorrection * 1000:F2} ms");

                await backend.PauseAsync(); await Until(() => backend.GetCurrentState()?.Playing == false);
                var paused = backend.GetCurrentState()!.Position; await Task.Delay(800);
                Check($"{fallback}: paused position stays fixed", (backend.GetCurrentState()!.Position - paused).Duration().TotalSeconds < .1);
                await backend.SeekAsync(TimeSpan.FromSeconds(30.5));
                await Until(() => Math.Abs(backend.GetCurrentState()!.Position.TotalSeconds - 30.5) < .25);
                Check($"{fallback}: fractional seek while paused", true);
                await backend.TogglePlayPauseAsync(); await Until(() => backend.GetCurrentState()?.Playing == true);
                Check($"{fallback}: native toggle", true);
                await backend.NextAsync(); await Until(() => backend.GetCurrentState() is { Title: { Length: > 0 } title } && title != initial.Title);
                Check($"{fallback}: native next track", !string.IsNullOrEmpty(backend.GetCurrentState()?.Title));
                await backend.PreviousAsync(); await Until(() => backend.GetCurrentState()?.Title == initial.Title);
                Check($"{fallback}: native previous track", true);
            }
            finally
            {
                await backend.PauseAsync();
                await backend.SeekAsync(initial.Position);
                if (initial.Playing) await backend.PlayAsync();
            }
            backend.Dispose(); backend.Dispose();
            Check($"{fallback}: clean repeatable shutdown", true);
        }
    }
    finally { Environment.SetEnvironmentVariable("OMNILYRICS_MEDIA_CONTROL", originalSetting); }
    if (MacOSMediaControlBackend.ResolveExecutable() is { } tool)
    {
        using var fallback = new MacOSMediaControlBackend(tool);
        await fallback.StartAsync(default);
        await Until(() => fallback.GetCurrentState() is { Title: { Length: > 0 } }, 10000);
        var initial = fallback.GetCurrentState()!;
        Check("Installed fallback independently reads Apple Music", initial.SourceApp == MacOSAppleEventsBackend.MusicBundle);
        try
        {
            await fallback.PlayAsync(); await Until(() => fallback.GetCurrentState()?.Playing == true);
            Check("Installed fallback independently controls play", true);
            await fallback.PauseAsync(); await Until(() => fallback.GetCurrentState()?.Playing == false);
            Check("Installed fallback independently controls pause", true);
            await fallback.SeekAsync(TimeSpan.FromSeconds(30.5));
            await Until(() => Math.Abs(fallback.GetCurrentState()!.Position.TotalSeconds - 30.5) < .3);
            Check("Installed fallback independently controls fractional seek", true);
        }
        finally
        {
            await fallback.PauseAsync(); await fallback.SeekAsync(initial.Position);
            if (initial.Playing) await fallback.PlayAsync();
        }
        fallback.Dispose(); fallback.Dispose();
        Check("Installed fallback independently shuts down cleanly", true);
    }
    Console.WriteLine($"{checks} live checks passed"); return;
}

using (var native = new MacOSAppleEventsBackend(MacOSAppleEventsBackend.MusicBundle))
{
    bool cleared = false;
    native.OnStateChanged += (_, state) => { if (state == null) cleared = true; };
    native.ProcessLine(JsonSerializer.Serialize(new { title = "Quotes \" / 中文\nline", artist = "Artist", album = "Album", duration = 180.5, position = 30.25, playing = false }));
    Check("Native JSON preserves Unicode and delimiters", native.GetCurrentState()?.Title == "Quotes \" / 中文\nline");
    Check("Native fractional seconds stay in seconds", native.GetCurrentState()?.Position.TotalSeconds == 30.25 && native.GetCurrentState()?.Duration.TotalSeconds == 180.5);
    native.GetCurrentState()!.Title = "mutated";
    Check("Native state is a snapshot", native.GetCurrentState()?.Title != "mutated");
    native.ProcessLine("{\"error\":-1743}");
    Check("Denied Automation clears stale media and exposes recovery", native.GetCurrentState() == null && native.ConnectionError != null && cleared);
    native.ProcessLine("{\"title\":\"Recovered\",\"artist\":\"Artist\",\"album\":\"Album\",\"duration\":180,\"position\":0,\"playing\":true}");
    Check("Permission recovery clears the error", native.ConnectionError == null && native.GetCurrentState()?.Playing == true);
    native.ProcessLine("null");
    Check("Closed player clears native state", native.GetCurrentState() == null);
}
var time = new ManualTime();
using (var native = new MacOSAppleEventsBackend(MacOSAppleEventsBackend.MusicBundle, time))
{
    void Report(double position, bool playing = true, string title = "Track") => native.ProcessLine(JsonSerializer.Serialize(
        new { title, artist = "Artist", album = "Album", duration = 180, position, playing }));
    Report(30);
    double previous = 30; bool monotonic = true;
    for (int i = 1; i <= 100; i++)
    {
        time.Advance(.1);
        if (i % 5 == 0) Report(30 + i / 10);
        var position = native.GetCurrentState()!.Position.TotalSeconds;
        monotonic &= position >= previous; previous = position;
    }
    Check("Repeated coarse native progress never rewinds between lyric lines", monotonic && Math.Abs(previous - 40) < .5);
    for (int i = 0; i < 20; i++) { time.Advance(.5); Report(40); }
    Check("Stalled native progress cannot extrapolate indefinitely", native.GetCurrentState()!.Position.TotalSeconds <= 42.1);
    Report(5, true, "Cadence test");
    var lastFrame = native.GetCurrentState()!.Position.TotalSeconds;
    double largestStep = 0, smallestStep = 1;
    for (int frame = 1; frame <= 1250; frame++)
    {
        time.Advance(.016);
        if (frame % 31 == 0) Report(5 + Math.Floor(frame * .016 + .3), true, "Cadence test");
        var current = native.GetCurrentState()!.Position.TotalSeconds;
        largestStep = Math.Max(largestStep, current - lastFrame);
        smallestStep = Math.Min(smallestStep, current - lastFrame);
        lastFrame = current;
    }
    Check("Coarse Events reports never step forward or stall a karaoke frame", smallestStep >= .0151 && largestStep <= .0169);
    Report(12.5);

    Check("Backward seek immediately replaces the interpolated position", native.GetCurrentState()!.Position.TotalSeconds == 12.5);
    Report(80.5);
    Check("Forward seek immediately replaces the interpolated position", native.GetCurrentState()!.Position.TotalSeconds == 80.5);
    Report(80.6, false); time.Advance(.5);
    Check("Pause uses the reported position without interpolation", native.GetCurrentState()!.Position.TotalSeconds == 80.6);
    Report(0, true, "New track");
    Check("Track change resets the native interpolation", native.GetCurrentState()!.Position == TimeSpan.Zero);
    time.Advance(6);
    Check("Unresponsive native source becomes unavailable for fallback", native.GetCurrentState() == null);
}

using (var fallback = new MacOSMediaControlBackend("missing-test-tool"))
{
    fallback.ProcessJsonLine("{\"payload\":{\"title\":\"Track\",\"artist\":\"Artist\",\"album\":\"Album\",\"durationMicros\":180000000,\"elapsedTimeMicros\":2000000,\"playing\":true}}");
    Check("Missing stream timestamp does not jump to track end", fallback.GetCurrentState()?.Position.TotalSeconds == 2);
    fallback.ProcessJsonLine("{\"payload\":{\"playing\":false}}");
    Check("Partial stream packet preserves metadata", fallback.GetCurrentState() is { Title: "Track", Playing: false });
    bool cleared = false; fallback.OnStateChanged += (_, state) => cleared |= state == null;
    fallback.ProcessJsonLine("{\"payload\":null}");
    Check("Empty stream emits a clear event", fallback.GetCurrentState() == null && cleared);
    await fallback.StartAsync(default); await Until(() => fallback.ConnectionError != null);
    fallback.Dispose(); fallback.Dispose();
    Check("Missing optional tool is observable and disposal is safe", true);
}

FakeBackend music = new(), spotify = new(), system = new(), cider = new();
using (var dynamic = new DynamicBackend(new() { ["AppleMusic"] = music, ["Spotify"] = spotify, ["MediaControl"] = system, ["CiderV3"] = cider }, true))
{
    music.Set(Song(MacOSAppleEventsBackend.MusicBundle, false));
    system.Set(Song(MacOSAppleEventsBackend.MusicBundle));
    await dynamic.PlayAsync();
    Check("Paused native state still outranks a stale playing fallback", music.Controls == 1 && system.Controls == 0);
    var nativeSong = Song(MacOSAppleEventsBackend.MusicBundle);
    nativeSong.Title = "Authoritative native title";
    music.Set(nativeSong);
    bool stable = true;
    for (int i = 0; i < 100; i++)
    {
        system.Set(i % 3 == 0 ? null : Song(MacOSAppleEventsBackend.MusicBundle, i % 2 == 0));
        stable &= dynamic.GetCurrentState()?.Title == nativeSong.Title;
    }
    Check("Duplicate fallback updates and clears never replace native lyrics", stable);
    system.Set(Song(MacOSAppleEventsBackend.MusicBundle));
    music.Set(Song(MacOSAppleEventsBackend.MusicBundle));
    spotify.Set(Song(MacOSAppleEventsBackend.SpotifyBundle));
    music.Set(Song(MacOSAppleEventsBackend.MusicBundle));
    await dynamic.PauseAsync();
    Check("Periodic samples do not steal control from newly playing Spotify", spotify.Controls == 1 && music.Controls == 1);
    spotify.Set(null); music.Set(null);
    await dynamic.PlayAsync();
    Check("Fallback is used when native state becomes unavailable", system.Controls == 1);
    cider.Set(Song("Cider", false)); system.Set(Song("sh.cider.Cider"));
    await dynamic.PlayAsync();
    Check("Cider Web API outranks the generic Cider connection", cider.Controls == 1 && system.Controls == 1);
    cider.Set(null); system.Set(Song("other.player"));
    await dynamic.PlayAsync();
    Check("Other players retain the optional fallback", system.Controls == 2);
    system.Set(null);
    Check("All closed players clear selected state", dynamic.GetCurrentState() == null);
}
var syncFailure = new FakeBackend { FailStart = 1 }; var asyncFailure = new FakeBackend { FailStart = 2 }; var healthy = new FakeBackend();
using (var dynamic = new DynamicBackend(new() { ["sync"] = syncFailure, ["async"] = asyncFailure, ["CiderV3"] = healthy }, true))
{
    await dynamic.StartAsync(default); await Until(() => healthy.Started && asyncFailure.Started);
    await Task.Delay(50);
    Check("Synchronous and asynchronous backend failure leave other backends running", healthy.Started && dynamic.ConnectionError != null);
    healthy.Set(Song("Cider")); await dynamic.PauseAsync();
    Check("Healthy connection remains controllable after startup failure", healthy.Controls == 1 && dynamic.ConnectionError == null);
}

var slow = new FakeBackend { FailStart = 3 };
using (var dynamic = new DynamicBackend(new() { ["slow"] = slow }, true))
{
    await dynamic.StartAsync(default);
    var clock = Stopwatch.StartNew(); dynamic.Dispose();
    Check("Unresponsive startup cannot block application shutdown", clock.Elapsed < TimeSpan.FromSeconds(1));
}

var unavailable = new MacEnvironmentStatus(false, false, null, "/fake/brew");
Check("Setup is required only after both connections fail", unavailable.NeedsInstallation
    && !(unavailable with { NativeAvailable = true }).NeedsInstallation
    && !(unavailable with { NativeAvailable = null }).NeedsInstallation
    && !(unavailable with { MediaControlAvailable = true }).NeedsInstallation
    && !(unavailable with { MediaControlDisabled = true }).NeedsInstallation);
int prompts = 0, installs = 0;
using (var output = new StringWriter())
{
    for (int i = 0; i < 2; i++)
        await MacEnvironment.HandleConsoleStatusAsync(unavailable, true, output, () => { prompts++; return "n"; }, () => { installs++; return Task.FromResult(0); });
    Check("Declining installation prompts again on the next launch", prompts == 2 && installs == 0);
    await MacEnvironment.HandleConsoleStatusAsync(unavailable, false, output, () => throw new Exception("Read redirected stdin"), () => throw new Exception("Unrequested install"));
    Check("Noninteractive startup reports recovery without reading stdin", output.ToString().Contains("brew install media-control"));
    await MacEnvironment.HandleConsoleStatusAsync(unavailable, true, output, () => "yes", () => { installs++; return Task.FromResult(0); });
    Check("Explicit console consent runs the installer once", installs == 1);
    await MacEnvironment.HandleConsoleStatusAsync(unavailable with { BrewPath = null }, true, output, () => throw new Exception("No brew"), () => throw new Exception("No brew"));
    Check("Missing Homebrew reports setup instructions without installing it", output.ToString().Contains("brew.sh"));
    await MacEnvironment.HandleConsoleStatusAsync(unavailable with { NativeAvailable = true }, true, output, () => throw new Exception("Healthy bridge prompted"), () => throw new Exception("Healthy bridge installed"));
    Check("Healthy native access never prompts for installation", installs == 1);
}

if (!OperatingSystem.IsWindows())
{
    var directory = Path.Combine(Path.GetTempPath(), "omnilyrics-mac-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var pidFile = Path.Combine(directory, "pid");
        var script = Path.Combine(directory, "media-control");
        await File.WriteAllTextAsync(script, $"#!/bin/sh\necho $$ > '{pidFile}'\nwhile :; do sleep 30; done\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var backend = new MacOSMediaControlBackend(script);
        await backend.StartAsync(default); await Until(() => File.Exists(pidFile));
        var pid = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim());
        var clock = Stopwatch.StartNew(); backend.Dispose();
        Check("Idle pipe cancellation finishes promptly", clock.Elapsed < TimeSpan.FromSeconds(3));
        bool exited;
        try { using var process = Process.GetProcessById(pid); exited = process.HasExited; }
        catch (ArgumentException) { exited = true; }
        Check("Stream child is reaped on shutdown", exited);
        await File.WriteAllTextAsync(script, "#!/bin/sh\necho command-failed >&2\nexit 7\n");
        var installOutput = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await File.WriteAllTextAsync(script, "#!/bin/sh\n[ \"$#\" = 2 ] && [ \"$1\" = install ] && [ \"$2\" = media-control ] || exit 9\necho output\necho diagnostic >&2\nexit 7\n");
        Check("Homebrew receives only fixed install arguments and reports failure", await MacEnvironment.InstallAsync(script, installOutput.Enqueue, default) == 7);
        Check("Installer captures both output streams", installOutput.Contains("output") && installOutput.Contains("diagnostic"));
        File.Delete(pidFile);
        await File.WriteAllTextAsync(script, $"#!/bin/sh\necho $$ > '{pidFile}'\nwhile :; do sleep 30; done\n");
        using (var cancel = new CancellationTokenSource())
        {
            var installing = MacEnvironment.InstallAsync(script, _ => { }, cancel.Token);
            await Until(() => File.Exists(pidFile));
            var installerPid = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim());
            clock.Restart(); cancel.Cancel();
            try { await installing; throw new Exception("Install did not cancel"); } catch (OperationCanceledException) { }
            try { using var child = Process.GetProcessById(installerPid); exited = child.HasExited; } catch (ArgumentException) { exited = true; }
            Check("Cancelled installation promptly reaps the installer", exited && clock.Elapsed < TimeSpan.FromSeconds(3));
        }
        await File.WriteAllTextAsync(script, "#!/bin/sh\necho command-failed >&2\nexit 7\n");
        using var failingCommand = new MacOSMediaControlBackend(script);
        try { await failingCommand.PauseAsync(); throw new Exception("Command failure was swallowed"); }
        catch (IOException error) { Check("Nonzero control exit reaches the caller", error.Message.Contains("7")); }
    }
    finally { Directory.Delete(directory, true); }
}
Console.WriteLine($"{checks} checks passed");

sealed class FakeBackend : BasePlayerBackend
{
    public PlayerState? State;
    public int Controls, FailStart;
    public bool Started;
    public void Set(PlayerState? state) { State = state; EmitStateChanged(state!); }
    public override PlayerState? GetCurrentState() => State;
    public override Task StartAsync(CancellationToken token)
    {
        Started = true;
        if (FailStart == 1) throw new System.ComponentModel.Win32Exception(2);
        return FailStart == 2 ? Task.FromException(new IOException("Async failure")) : FailStart == 3 ? Task.Delay(Timeout.Infinite, token) : Task.CompletedTask;
    }
    public override Task PlayAsync() { Controls++; return Task.CompletedTask; }
    public override Task PauseAsync() => PlayAsync();
    public override Task TogglePlayPauseAsync() => PlayAsync();
    public override Task NextAsync() => PlayAsync();
    public override Task PreviousAsync() => PlayAsync();
    public override Task SeekAsync(TimeSpan position) => PlayAsync();
}

sealed class ManualTime : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
}
