using System.Text.Json;
using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Core;

public enum ServiceRole { Cli = 1, Tui = 2, Gui = 3 }

/// <summary>Per-user, per-endpoint candidates; an OS-held lease makes ownership exclusive even after a crash.</summary>
internal sealed class ServiceElection : IDisposable
{
    private readonly string _directory;
    private readonly string _candidate;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly ServiceRole _role;
    private long _nextHeartbeat;
    private sealed record Candidate(string Id, ServiceRole Role);

    public ServiceElection(ServerSettings settings, ServiceRole role)
    {
        _role = role;
        _directory = Path.Combine(UserConfiguration.DirectoryPath, "runtime", $"service-{settings.HttpPort}-{settings.UdpPort}");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_directory);
        else Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _candidate = Path.Combine(_directory, _id + ".json");
        File.WriteAllText(_candidate, JsonSerializer.Serialize(new Candidate(_id, role)));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_candidate, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public void Heartbeat()
    {
        if (Environment.TickCount64 < _nextHeartbeat) return;
        _nextHeartbeat = Environment.TickCount64 + 1000;
        // A suspended process may resume after another candidate removed its stale heartbeat.
        if (!File.Exists(_candidate)) File.WriteAllText(_candidate, JsonSerializer.Serialize(new Candidate(_id, _role)));
        File.SetLastWriteTimeUtc(_candidate, DateTime.UtcNow);
    }

    public FileStream? TryAcquire()
    {
        var candidates = new List<Candidate>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromSeconds(5))
                { File.Delete(file); continue; }
                if (new FileInfo(file).Length > 256) continue;
                if (JsonSerializer.Deserialize<Candidate>(File.ReadAllText(file)) is { Role: >= ServiceRole.Cli and <= ServiceRole.Gui } candidate)
                    candidates.Add(candidate);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        if (candidates.OrderByDescending(c => c.Role).ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault()?.Id != _id) return null;
        try
        {
            // Never delete the lock file: unlinking a locked inode permits two simultaneous owners on Unix.
            return new FileStream(Path.Combine(_directory, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { return null; }
    }

    public void Dispose()
    {
        try { File.Delete(_candidate); } catch (IOException) { }
    }
}
