using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Lyrics;
using OmniLyrics.Core.Lyrics.Models;

namespace OmniLyrics.Core.Shared;

/// <summary>Bounded cache shared by foreground loads and queue prefetches.</summary>
public sealed class LyricsCache
{
    private static readonly LyricsService Service = new();
    public static LyricsCache Shared { get; } = new((state, karaoke, settings) => Service.SearchLyricLinesAsync(state, karaoke, settings),
        Path.Combine(UserConfiguration.DirectoryPath, "cache", "lyrics"), settings: LyricsSearchPreferences.Read);
    private const int Capacity = 256;
    private readonly Func<PlayerState, bool, LyricsSettings, Task<List<LyricsLine>?>> _load;
    private readonly Func<LyricsSettings> _settings;
    private readonly string? _directory;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly Dictionary<string, Task<List<LyricsLine>?>> _pending = new();
    private readonly SemaphoreSlim _prefetchSlot = new(1, 1);
    public sealed record Entry(int Version, DateTimeOffset Expires, List<LyricsLine>? Lines);

    public LyricsCache(Func<PlayerState, bool, Task<List<LyricsLine>?>> load,
        string? directory = null, TimeProvider? time = null, Func<LyricsSettings>? settings = null)
        : this((state, karaoke, _) => load(state, karaoke), directory, time, settings) { }

    public LyricsCache(Func<PlayerState, bool, LyricsSettings, Task<List<LyricsLine>?>> load,
        string? directory = null, TimeProvider? time = null, Func<LyricsSettings>? settings = null)
    { _load = load; _directory = directory; _time = time ?? TimeProvider.System; _settings = settings ?? (() => new(true, 5)); }

    public string SearchIdentity => LyricsSearchPreferences.Key(_settings());

    public static string TrackKey(PlayerState state)
    {
        // Cider Web API and MPRIS describe the same tracks. Keep other player
        // sources distinct because some supply their own embedded lyrics.
        var source = state.SourceApp ?? "";
        if (source.Contains("cider", StringComparison.OrdinalIgnoreCase)) source = "Cider";
        static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant();
        return "match-v3-bilingual|" + JsonSerializer.Serialize(new[] { Normalize(source), Normalize(state.Title),
            string.Join('\u001f', state.Artists.Select(Normalize)), Normalize(state.Album),
            Math.Round(state.Duration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    private static string Key(PlayerState state, bool karaoke, string policy) => TrackKey(state) + "|karaoke:" + karaoke + "|" + policy;

    public bool IsFresh(PlayerState state, bool karaoke)
    {
        var policy = SearchIdentity;
        lock (_gate) return Find(Key(state, karaoke, policy)) != null
            || !karaoke && Find(Key(state, true, policy)) != null;
    }

    private Entry? Find(string key)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.Expires > _time.GetUtcNow()) return entry;
        _entries.Remove(key);
        return null;
    }

    public Task<List<LyricsLine>?> GetAsync(PlayerState state, bool karaoke)
    {
        var settings = _settings();
        var policy = LyricsSearchPreferences.Key(settings);
        var key = Key(state, karaoke, policy);
        var timedKey = Key(state, true, policy);
        TaskCompletionSource<List<LyricsLine>?> completion;
        lock (_gate)
        {
            // Word-timed lyrics also work in a line-only frontend.
            if (!karaoke && Find(timedKey) is { Lines: { Count: > 0 } } timed)
                return Task.FromResult<List<LyricsLine>?>(timed.Lines);
            if (Find(key) is { } cached) return Task.FromResult(cached.Lines);
            if (_pending.TryGetValue(key, out var pending)) return pending;
            if (!karaoke && _pending.TryGetValue(timedKey, out var timedPending)) return timedPending;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[key] = completion.Task;
        }
        _ = LoadAsync(state.DeepCopy(), karaoke, settings, key, timedKey, completion);
        return completion.Task;
    }

    public async Task PrefetchAsync(PlayerState state, CancellationToken token)
    {
        await _prefetchSlot.WaitAsync(token);
        try { token.ThrowIfCancellationRequested(); await GetAsync(state, true); }
        finally { _prefetchSlot.Release(); }
    }

    private async Task LoadAsync(PlayerState state, bool karaoke, LyricsSettings settings, string key, string timedKey,
        TaskCompletionSource<List<LyricsLine>?> completion)
    {
        List<LyricsLine>? lines = null;
        try
        {
            var stored = (!karaoke ? Read(timedKey) : null) ?? Read(key);
            lines = stored?.Lines ?? await _load(state, karaoke, settings);
            if (lines is not { Count: > 0 }) lines = null;
            var lifetime = lines == null ? TimeSpan.FromMinutes(1)
                : karaoke && !LyricsService.HasWordTiming(lines) ? TimeSpan.FromMinutes(10) : TimeSpan.FromDays(7);
            var entry = new Entry(1, stored?.Expires ?? _time.GetUtcNow().Add(lifetime), lines);
            lock (_gate)
            {
                if (_entries.Count >= Capacity) _entries.Remove(_entries.Keys.First());
                _entries[key] = entry;
            }
            if (stored == null && lines != null) Write(key, entry);
        }
        catch
        {
            lock (_gate)
            {
                if (_entries.Count >= Capacity) _entries.Remove(_entries.Keys.First());
                _entries[key] = new(1, _time.GetUtcNow().AddMinutes(1), null);
            }
        }
        finally
        {
            lock (_gate) { completion.TrySetResult(lines); _pending.Remove(key); }
        }
    }

    private string FileName(string key) => Path.Combine(_directory!,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");

    private Entry? Read(string key)
    {
        if (_directory == null) return null;
        try
        {
            var path = FileName(key);
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) return null;
            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
            return entry is { Version: 1, Lines.Count: > 0 } && entry.Expires > _time.GetUtcNow() ? entry : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private void Write(string key, Entry entry)
    {
        if (_directory == null) return;
        string? temporary = null;
        try
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_directory);
            else Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options)) JsonSerializer.Serialize(file, entry);
            File.Move(temporary, FileName(key), overwrite: true);
            foreach (var old in new DirectoryInfo(_directory).EnumerateFiles("*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(Capacity)) old.Delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Cache failure must not hide lyrics. */ }
        finally
        {
            try { if (temporary != null) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
