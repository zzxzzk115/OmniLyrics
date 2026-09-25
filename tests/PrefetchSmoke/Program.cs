using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Lyrics;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Shared;

var passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name); passed++;
}
async Task Until(Func<bool> condition)
{
    for (var i = 0; i < 80; i++) { if (condition()) return; await Task.Delay(50); }
    throw new Exception("Condition timed out");
}
PlayerState Track(string name, string source = "Test player") => new()
{ Title = name, Artists = ["Test artist"], Album = "Test album", SourceApp = source };
List<LyricsLine> Timed(string text) => [new(TimeSpan.Zero, text, [new(TimeSpan.Zero, TimeSpan.FromSeconds(1), text)])];
List<LyricsLine> Plain(string text) => [new(TimeSpan.Zero, text, null)];
Lyricify.Lyrics.Searchers.QQMusicSearchResult Candidate(string title = "Song", string artist = "Artist", int duration = 200000, string album = "Album")
    => new(title, [artist], album, null, duration, "test", "test");
var expected = new PlayerState { Title = "Song", Artists = ["Artist"], Album = "Album", Duration = TimeSpan.FromSeconds(200) };
Check("Exact recording is accepted", TrackMatch.IsSuitable(expected, Candidate()));
Check("Same-title wrong artist is rejected", !TrackMatch.IsSuitable(expected, Candidate(artist: "Other artist")));
Check("Same artist different song is rejected", !TrackMatch.IsSuitable(expected, Candidate(title: "Another song")));
Check("Live recordings do not replace the studio version", !TrackMatch.IsSuitable(expected, Candidate(title: "Song (Live)")));
Check("Album live marker also excludes incompatible recording", !TrackMatch.IsSuitable(expected, Candidate(album: "Live at home")));
Check("Remixes do not replace the original version", !TrackMatch.IsSuitable(expected, Candidate(title: "Song (Remix)")));
Check("A materially different duration is rejected", !TrackMatch.IsSuitable(expected, Candidate(duration: 218000)));
Check("Small trailing-silence differences are tolerated", TrackMatch.IsSuitable(expected, Candidate(duration: 202000)));
Check("Remaster metadata does not prevent a matching duration", TrackMatch.IsSuitable(expected, Candidate(title: "Song (2020 Remaster)")));
var sharedGuest = new Lyricify.Lyrics.Searchers.QQMusicSearchResult("Song", ["Other artist", "Guest"], "Album", null, 200000, "test", "test");
expected.Artists.Add("Guest");
Check("A shared guest artist cannot admit a different lead singer", !TrackMatch.IsSuitable(expected, sharedGuest));
expected.Title = "听见风"; expected.Artists = ["测试歌手"];
Check("Traditional and simplified metadata can match", TrackMatch.IsSuitable(expected, Candidate("聽見風", "測試歌手")));
expected.Artists = [];
Check("Missing artist metadata is not guessed", !TrackMatch.IsSuitable(expected, Candidate("聽見風", "測試歌手")));
var original = LyricsService.ParseLyrics("[00:01.00]First line\n[00:05.00]Second line\n[00:09.00]Third line", Lyricify.Lyrics.Models.LyricsRawTypes.Lrc)!;
var bilingual = LyricsService.AttachTranslation(original, "[00:01.05]第一行\n[00:09.00]第三行")!;
Check("Translations align by time, including small timestamp rounding", bilingual[0].Translation == "第一行" && bilingual[2].Translation == "第三行");
Check("A missing translation does not shift subsequent lines", bilingual[1].Translation == null);
Check("Large translation offsets are not assigned by line index", LyricsService.AttachTranslation(original, "[00:03.00]偏移译文")!.All(l => l.Translation == null));
Check("Identical text is not displayed twice as translation", LyricsService.AttachTranslation(original, "[00:01.00]First line")![0].Translation == null);
Check("Malformed translation does not hide original lyrics", LyricsService.AttachTranslation(original, "invalid translation")![0].Text == "First line");
Check("Bilingual lyrics survive JSON protocol serialization", System.Text.Json.JsonSerializer.Deserialize<List<LyricsLine>>(System.Text.Json.JsonSerializer.Serialize(bilingual))![0].Translation == "第一行");
var line = Plain("line fallback"); var timed = Timed("word timing");
var attempts = 0;
var service = new LyricsService([
    (s,k) => Task.FromResult<List<LyricsLine>?>(line),
    (s,k) => { attempts++; return Task.FromResult<List<LyricsLine>?>(timed); }
]);
Check("Karaoke beats an earlier source's LRC", ReferenceEquals(await service.SearchLyricLinesAsync(Track("A"), true), timed) && attempts == 1);
var resilient = new LyricsService([
    (s,k) => throw new HttpRequestException(),
    (s,k) => Task.FromResult<List<LyricsLine>?>(line),
    (s,k) => Task.FromResult<List<LyricsLine>?>(null)
]);
Check("Source failures retain the final line fallback", ReferenceEquals(await resilient.SearchLyricLinesAsync(Track("A"), true), line));
Check("Zero duration tokens are not genuine karaoke", !LyricsService.HasWordTiming([new(TimeSpan.Zero, "test", [new(TimeSpan.Zero, TimeSpan.Zero, "test")])]));
var gate = new TaskCompletionSource<List<LyricsLine>?>(TaskCreationOptions.RunContinuationsAsynchronously);
var loads = 0;
var cache = new LyricsCache((s,k) => { Interlocked.Increment(ref loads); return gate.Task; });
var prefetch = cache.PrefetchAsync(Track("B"), default);
var foreground = cache.GetAsync(Track("B"), true);
Check("Foreground joins an in-flight prefetch", loads == 1 && !foreground.IsCompleted);
gate.SetResult(timed); await prefetch;
Check("Joined prefetch delivers the same timed result", ReferenceEquals(await foreground, timed));
var manager = new LyricsManager(cache: cache);
await manager.UpdateAsync(Track("B"), true);
Check("Switching to a prefetched song needs no provider request", loads == 1 && !manager.IsLoading && ReferenceEquals(manager.Current, timed));
Check("Line frontend reuses word-timed cache", ReferenceEquals(await cache.GetAsync(Track("B"), false), timed) && loads == 1);
var queueLoads = new List<string>();
var slow = new TaskCompletionSource<List<LyricsLine>?>(TaskCreationOptions.RunContinuationsAsynchronously);
var priority = new LyricsCache((s,k) => { lock (queueLoads) queueLoads.Add(s.Title!); return s.Title == "Slow" ? slow.Task : Task.FromResult<List<LyricsLine>?>(Timed(s.Title!)); });
var bg = priority.PrefetchAsync(Track("Slow"), default);
var fg = await priority.GetAsync(Track("Current"), true);
Check("Current song does not wait behind a different prefetch", fg![0].Text == "Current" && !bg.IsCompleted);
var bg2 = priority.PrefetchAsync(Track("Later"), default);
await Task.Delay(50);
Check("Only one background song is fetched at once", !queueLoads.Contains("Later"));
slow.SetResult(timed); await Task.WhenAll(bg, bg2);
Check("Background queue proceeds after its slot is free", queueLoads.Contains("Later"));
Check("Cider API and system connection share a track key", LyricsCache.TrackKey(Track("A", "Cider")) == LyricsCache.TrackKey(Track("A", "org.mpris.MediaPlayer2.cider")));
Check("Unrelated player sources retain separate keys", LyricsCache.TrackKey(Track("A", "Other")) != LyricsCache.TrackKey(Track("A", "YesPlayMusic")));
var clock = new ManualClock();
var failedLoads = 0;
var failures = new LyricsCache((s,k) => Task.FromResult<List<LyricsLine>?>(++failedLoads == 1 ? null : timed), time: clock);
await failures.GetAsync(Track("Retry"), true); await failures.GetAsync(Track("Retry"), true);
Check("Transient failure is briefly cached", failedLoads == 1);
clock.Advance(TimeSpan.FromMinutes(2));
Check("Failure expires and can recover", await failures.GetAsync(Track("Retry"), true) != null && failedLoads == 2);
var config = Path.Combine(Path.GetTempPath(), "omnilyrics-prefetch-" + Guid.NewGuid().ToString("N"));
var oldConfig = Environment.GetEnvironmentVariable("OMNILYRICS_CONFIG_DIR");
Environment.SetEnvironmentVariable("OMNILYRICS_CONFIG_DIR", config);
try
{
    var defaults = new LyricsSettings(true, 5);
    Check("Older configurations retain karaoke-first defaults and all three sources", UserConfiguration.LoadLyrics() == defaults);
    var configured = defaults with { SearchStrategy = "quick", MatchMode = "strict", PlayerSource = false, QQMusicSource = false, PreferredSource = "netease" };
    UserConfiguration.SaveLyrics(configured);
    Check("All matching and source preferences round-trip", UserConfiguration.LoadLyrics() == configured);
    var untouched = UserConfiguration.ReadConfigurationText();
    var invalidPolicies = 0;
    foreach (var invalid in new[] { defaults with { SearchStrategy = "unknown" }, defaults with { MatchMode = "unknown" },
        defaults with { PreferredSource = "unknown" }, defaults with { PlayerSource = false, QQMusicSource = false, NeteaseSource = false } })
        try { UserConfiguration.SaveLyrics(invalid); } catch (ArgumentException) { invalidPolicies++; }
    Check("Invalid policies and empty sources never overwrite saved settings", invalidPolicies == 4 && UserConfiguration.ReadConfigurationText() == untouched);
    var recording = new PlayerState { Title = "Song", Artists = ["Artist"], Album = "Album", Duration = TimeSpan.FromSeconds(200) };
    Check("Strict matching requires the same album", !TrackMatch.IsSuitable(recording, Candidate(album: "Compilation"), "strict")
        && TrackMatch.IsSuitable(recording, Candidate(album: "Compilation"), "balanced"));
    Check("Strict matching tightens duration tolerance", !TrackMatch.IsSuitable(recording, Candidate(duration: 203000), "strict")
        && TrackMatch.IsSuitable(recording, Candidate(duration: 203000), "balanced"));
    Check("Strict matching rejects unknown durations", !TrackMatch.IsSuitable(recording, Candidate(duration: 0), "strict"));
    Check("Strict matching accepts compatible recordings", TrackMatch.IsSuitable(recording, Candidate(), "strict"));
    var searcher = new SearchFixture();
    Check("Basic search stops after unsuitable first results", await LyricsService.FindMatchAsync(recording, searcher, defaults with { SearchStrategy = "quick" }) == null && searcher.Calls == 1);
    searcher.Calls = 0;
    Check("Fallback search finds a suitable recording in the broader search", await LyricsService.FindMatchAsync(recording, searcher, defaults) != null && searcher.Calls > 1);
    var sourceCalls = new List<string>();
    var named = new LyricsService(new Dictionary<string, Func<PlayerState, bool, LyricsSettings, Task<List<LyricsLine>?>>>
    {
        ["player"] = (s,k,o) => { sourceCalls.Add("player"); return Task.FromResult<List<LyricsLine>?>(line); },
        ["qq"] = (s,k,o) => { sourceCalls.Add("qq"); return Task.FromResult<List<LyricsLine>?>(timed); },
        ["netease"] = (s,k,o) => { sourceCalls.Add("netease"); return Task.FromResult<List<LyricsLine>?>(line); }
    });
    var selected = await named.SearchLyricLinesAsync(recording, true, defaults with { PreferredSource = "netease" });
    Check("Preferred sources retain karaoke priority over earlier line lyrics", ReferenceEquals(selected, timed) && sourceCalls.SequenceEqual(new[] { "player", "netease", "qq" }));
    sourceCalls.Clear();
    await named.SearchLyricLinesAsync(recording, true, configured);
    Check("Disabled lyric providers receive no requests", sourceCalls.SequenceEqual(new[] { "netease" }));
    var policy = defaults;
    var policyLoads = 0;
    var policyDisk = Path.Combine(config, "policy-cache");
    var oldPolicyResult = new TaskCompletionSource<List<LyricsLine>?>(TaskCreationOptions.RunContinuationsAsynchronously);
    var policyCache = new LyricsCache((s,k,o) => { policyLoads++; return o.MatchMode == "balanced" ? oldPolicyResult.Task : Task.FromResult<List<LyricsLine>?>(Timed(o.MatchMode)); }, policyDisk, settings: () => policy);
    var policyManager = new LyricsManager(cache: policyCache);
    var oldPolicyRequest = policyManager.UpdateAsync(recording, true);
    policy = configured;
    await policyManager.UpdateAsync(recording, true);
    oldPolicyResult.SetResult(Timed("old policy")); await oldPolicyRequest;
    Check("Changing policy reloads the current song and rejects late old results", policyManager.Current?[0].Text == "strict" && policyLoads == 2);
    policy = defaults;
    Check("Old-policy lyrics are not exposed while settings change", policyManager.Capture(recording).Lines == null);
    var reopenedPolicy = new LyricsCache((s,k,o) => { policyLoads++; return Task.FromResult<List<LyricsLine>?>(Timed("fresh")); }, policyDisk, settings: () => configured with { NeteaseSource = false, QQMusicSource = true });
    await reopenedPolicy.GetAsync(recording, true);
    Check("Disk cache cannot bypass a changed source selection", policyLoads == 3);
    Check("Queue size changes preserve matching cache identity", LyricsSearchPreferences.Key(defaults) == LyricsSearchPreferences.Key(defaults with { PrefetchCount = 10, Prefetch = false }));
    UserConfiguration.SaveLyrics(defaults);
    var savedAppearance = new AppearanceSettings("portrait", true, false, false, true, true, 44, 24, "#DDEEAA", "#ABCDEF", "#121212", .5, false);
    UserConfiguration.SaveAppearance(savedAppearance);
    UserConfiguration.SaveLanguage("zh-CN"); Localization.Reload();
    Check("Appearance values and blur toggle persist alongside language", UserConfiguration.LoadAppearance() == savedAppearance && Localization.Get("Settings") == "设置");
    UserConfiguration.SaveLyrics(new(true, 5));
    Check("Saving lyric options preserves appearance settings", UserConfiguration.LoadAppearance() == savedAppearance);
    UserConfiguration.SaveLanguage("en"); Localization.Reload();
    Check("English preference applies to shared localization", Localization.Get("Settings") == "Settings");
    var rejected = 0;
    foreach (var invalid in new[] { savedAppearance with { FontSize = double.NaN }, savedAppearance with { BackgroundOpacity = 2 }, savedAppearance with { TextColor = "invalid" } })
        try { UserConfiguration.SaveAppearance(invalid); } catch (ArgumentException) { rejected++; }
    Check("Invalid colors, sizes and opacity do not overwrite saved preferences", rejected == 3 && UserConfiguration.LoadAppearance() == savedAppearance);
    var blue = ThemePalette.BuiltIn["LightBlue"];
    var blueAppearance = blue.Apply(savedAppearance);
    UserConfiguration.SaveAppearance(blueAppearance);
    Check("A light theme persists without changing layout, fonts or window behavior", UserConfiguration.LoadAppearance() == blueAppearance
        && blueAppearance.Preset == savedAppearance.Preset && blueAppearance.FontSize == savedAppearance.FontSize
        && blueAppearance.Locked == savedAppearance.Locked && blueAppearance.UseBlur == savedAppearance.UseBlur);
    UserConfiguration.SaveThemePreset(new("Evening", blue));
    var custom = blue with { AccentColor = "#5533AA", HighlightColor = "#6633AA" };
    UserConfiguration.SaveThemePreset(new("evening", custom));
    Check("A named custom palette can be saved, reopened and updated without duplicate names",
        UserConfiguration.LoadThemePresets().Single() == new NamedThemePreset("evening", custom));
    Check("Saving a palette preserves active appearance and other preferences",
        UserConfiguration.LoadAppearance() == blueAppearance && UserConfiguration.LoadLyrics() == new LyricsSettings(true, 5));
    var beforeInvalid = UserConfiguration.ReadConfigurationText();
    rejected = 0;
    foreach (var invalid in new[] { new NamedThemePreset("", custom), new NamedThemePreset("bad", custom with { ThemeMode = "purple" }),
                 new NamedThemePreset("bad", custom with { AccentColor = "red" }) })
        try { UserConfiguration.SaveThemePreset(invalid); } catch (ArgumentException) { rejected++; }
    try { UserConfiguration.SaveConfigurationText("{\"themePresets\":[{\"name\":\"broken\",\"palette\":null}]}"); }
    catch (ArgumentException) { rejected++; }
    Check("Invalid palette names, modes, colors and editor input leave the file untouched",
        rejected == 4 && UserConfiguration.ReadConfigurationText() == beforeInvalid);
    UserConfiguration.DeleteThemePreset("EVENING");
    Check("Deleting a saved palette keeps the active appearance", UserConfiguration.LoadThemePresets().Count == 0
        && UserConfiguration.LoadAppearance() == blueAppearance);
    var disk = Path.Combine(config, "cache");
    var diskCache = new LyricsCache((s,k) => Task.FromResult<List<LyricsLine>?>(timed), disk, clock);
    await diskCache.PrefetchAsync(Track("Disk"), default);
    var diskLoads = 0;
    var reopened = new LyricsCache((s,k) => { diskLoads++; return Task.FromResult<List<LyricsLine>?>(null); }, disk, clock);
    var fromDisk = await reopened.GetAsync(Track("Disk"), true);
    Check("Another session reads cached timing from disk", fromDisk?[0].Tokens?[0].Duration == TimeSpan.FromSeconds(1) && diskLoads == 0);
    clock.Advance(TimeSpan.FromDays(8));
    await reopened.GetAsync(Track("Disk"), true);
    Check("Successful disk cache expires", diskLoads == 1);
    File.WriteAllText(Directory.GetFiles(disk, "*.json")[0], "broken-json");
    Check("Damaged cache falls back to a provider", (await new LyricsCache((s,k) => Task.FromResult<List<LyricsLine>?>(timed), disk).GetAsync(Track("Disk"), true)) != null);
    var fallbackCache = new LyricsCache((s,k) => Task.FromResult<List<LyricsLine>?>(line), time: clock);
    await fallbackCache.GetAsync(Track("Fallback"), true); clock.Advance(TimeSpan.FromMinutes(11));
    Check("LRC fallback expires early so karaoke can be retried", !fallbackCache.IsFresh(Track("Fallback"), true));
    var stale = new TaskCompletionSource<List<LyricsLine>?>(TaskCreationOptions.RunContinuationsAsynchronously);
    var displayed = new LyricsManager((s,k) => s.Title == "Old" ? stale.Task : Task.FromResult<List<LyricsLine>?>(Timed(s.Title!)));
    var old = displayed.UpdateAsync(Track("Old"), true);
    await displayed.UpdateAsync(Track("New"), true); stale.SetResult(Timed("Old")); await old;
    Check("Late results do not replace the current track", displayed.Current?[0].Text == "New");
    UserConfiguration.SaveLyrics(new(true, 2));
    var calls = new List<string>();
    var workerCache = new LyricsCache((s,k) => { lock (calls) calls.Add(s.Title!); return Task.FromResult<List<LyricsLine>?>(Timed(s.Title!)); });
    var workerManager = new LyricsManager(cache: workerCache);
    var backend = new QueueBackend(Track("Now"), [Track("Next"), Track("Then"), Track("Beyond limit")]);
    await workerManager.UpdateAsync(backend.State, true);
    using (var worker = new LyricsPrefetcher(backend, workerManager, default))
    {
        await Until(() => { lock(calls) return calls.Contains("Then"); });
        Check("Generic queue prefetch respects configured limit", !calls.Contains("Beyond limit") && backend.LastLimit == 2);
        Check("Prefetch does not change current lyrics", workerManager.Current?[0].Text == "Now");
        backend.State = Track("Next");
        await workerManager.UpdateAsync(backend.State, true);
        Check("Next song displays immediately from queue cache", workerManager.Current?[0].Text == "Next" && calls.Count(s => s == "Next") == 1);
    }
    var terminalManager = new LyricsManager((s,k) => Task.FromResult<List<LyricsLine>?>(s.Title == "Known" ? Timed("visible lyric") : null));
    var terminalBackend = new QueueBackend(Track("Known"), []);
    var terminal = new TerminalFixture(terminalBackend, terminalManager);
    await terminalManager.UpdateAsync(terminalBackend.State, true); terminal.Frame();
    Check("Terminal renders the known song", terminal.Text.Contains("visible lyric"));
    terminalBackend.State = Track("Missing"); await terminalManager.UpdateAsync(terminalBackend.State, true); terminal.Frame();
    Check("Terminal clears previous lyrics when matching finds nothing", !terminal.Text.Contains("visible lyric") && terminal.Text.Contains(Localization.Get("NoLyrics")));
    UserConfiguration.SaveLyrics(new(false, 2)); backend.QueueCalls = 0;
    using (var worker = new LyricsPrefetcher(backend, workerManager, default))
    {
        await Task.Delay(600);
        Check("Disabled prefetch does not query a player queue", backend.QueueCalls == 0);
    }
}
finally
{
    Environment.SetEnvironmentVariable("OMNILYRICS_CONFIG_DIR", oldConfig);
    Directory.Delete(config, true);
}
Console.WriteLine($"{passed} prefetch checks passed");

sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}
sealed class QueueBackend(PlayerState state, IReadOnlyList<PlayerState> queue) : BasePlayerBackend, IPlaybackQueueSource
{
    public PlayerState State = state;
    public int LastLimit, QueueCalls;
    public override PlayerState? GetCurrentState() => State;
    public Task<IReadOnlyList<PlayerState>> GetUpcomingTracksAsync(int limit, CancellationToken token = default)
    { LastLimit = limit; QueueCalls++; return Task.FromResult<IReadOnlyList<PlayerState>>(queue.Take(limit).ToList()); }
    public override Task StartAsync(CancellationToken token) => Task.CompletedTask;
    public override Task PlayAsync() => Task.CompletedTask;
    public override Task PauseAsync() => Task.CompletedTask;
    public override Task TogglePlayPauseAsync() => Task.CompletedTask;
    public override Task NextAsync() => Task.CompletedTask;
    public override Task PreviousAsync() => Task.CompletedTask;
    public override Task SeekAsync(TimeSpan position) => Task.CompletedTask;
}

sealed class SearchFixture : Lyricify.Lyrics.Searchers.Searcher
{
    public int Calls;
    public override string Name => "test";
    public override string DisplayName => "Test";
    public override Lyricify.Lyrics.Searchers.Searchers SearcherType => default;
    public override Task<List<Lyricify.Lyrics.Searchers.ISearchResult>?> SearchForResults(string query)
    {
        Calls++;
        return Task.FromResult<List<Lyricify.Lyrics.Searchers.ISearchResult>?>([
            new Lyricify.Lyrics.Searchers.QQMusicSearchResult(query.Contains("Album") ? "Other" : "Song", ["Artist"], "Album", null, 200000, "test", "test")]);
    }
}

sealed class TerminalFixture(IPlayerBackend backend, LyricsManager manager) : OmniLyrics.Core.Cli.BaseLyricsCli(backend, manager)
{
    public string Text = "";
    public void Frame() => RenderLyricsFrame();
    protected override Task RedrawScreenAsync(string a, string b, string c, List<string>? lyrics)
    { Text = a + b + c + string.Join("\n", lyrics ?? []); return Task.CompletedTask; }
}
