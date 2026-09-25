using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Shared;

if (args.FirstOrDefault() == "child")
{
    var folder = args[1]; var id = args[2]; var role = Enum.Parse<ServiceRole>(args[3]);
    var port = int.Parse(args[4]); var udp = int.Parse(args[5]);
    Environment.SetEnvironmentVariable("OMNILYRICS_CONFIG_DIR", folder);
    var starts = 0; var loads = 0; var pauses = 0;
    var manager = new LyricsManager((s,k) => { Interlocked.Increment(ref loads); return Task.FromResult<List<LyricsLine>?>([new(TimeSpan.Zero, "shared words", [new(TimeSpan.Zero, TimeSpan.FromSeconds(1), "shared words")])]); });
    await using var session = new SharedPlayerSession(() => new FakeBackend(() => starts++, () => pauses++), role,
        () => new("127.0.0.1", port, udp, "127.0.0.1"), lyrics: manager);
    File.WriteAllText(Path.Combine(folder, id + ".ready"), "");
    while (!File.Exists(Path.Combine(folder, "start"))) await Task.Delay(20);
    await session.StartAsync(default);
    while (!File.Exists(Path.Combine(folder, id + ".stop")))
    {
        var status = new Status(session.OwnsService, session.RemoteSnapshot?.OwnerRole, starts, loads, pauses,
            session.CaptureLyrics(session.GetCurrentState()).Lines?.FirstOrDefault()?.Text);
        var path = Path.Combine(folder, id + ".status");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(status)); File.Move(path + ".tmp", path, true);
        await Task.Delay(80);
    }
    return;
}

var passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name); passed++;
}
async Task Until(Func<bool> predicate, int seconds = 15)
{
    for (var i = 0; i < seconds * 20; i++) { if (predicate()) return; await Task.Delay(50); }
    throw new Exception("Condition timed out");
}
int FreeTcpPort()
{
    var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop(); return port;
}
int FreeUdpPort()
{
    using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
}
var folderRoot = Path.Combine(Path.GetTempPath(), "omnilyrics-election-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folderRoot);
var children = new List<Process>();
var httpPort = FreeTcpPort(); var udpPort = FreeUdpPort();
Process Start(string id, ServiceRole role)
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var value in new[] { "child", folderRoot, id, role.ToString(), httpPort.ToString(), udpPort.ToString() }) start.ArgumentList.Add(value);
    var process = Process.Start(start)!; children.Add(process);
    process.BeginOutputReadLine(); process.BeginErrorReadLine();
    return process;
}
Status? Read(string id)
{
    try { return JsonSerializer.Deserialize<Status>(File.ReadAllText(Path.Combine(folderRoot, id + ".status"))); }
    catch (Exception e) when (e is IOException or JsonException) { return null; }
}
async Task Stop(string id, Process process)
{
    File.WriteAllText(Path.Combine(folderRoot, id + ".stop"), "");
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    Check(id + " exits cleanly and releases ownership", process.ExitCode == 0);
}
try
{
    var cli = Start("cli", ServiceRole.Cli); var tui = Start("tui", ServiceRole.Tui); var gui = Start("gui", ServiceRole.Gui);
    await Until(() => new[] { "cli", "tui", "gui" }.All(id => File.Exists(Path.Combine(folderRoot, id + ".ready"))));
    File.WriteAllText(Path.Combine(folderRoot, "start"), "");
    await Until(() => Read("gui") is { Owner: true, Lyric: "shared words" } && Read("tui")?.Remote == "gui" && Read("cli")?.Remote == "gui");
    Check("Concurrent GUI, TUI and CLI elect GUI", Read("gui")!.Starts == 1 && Read("tui")!.Starts == 0 && Read("cli")!.Starts == 0);
    Check("Followers reuse lyrics without provider requests", Read("gui")!.Loads == 1 && Read("tui")!.Loads == 0 && Read("cli")!.Loads == 0 && Read("cli")!.Lyric == "shared words");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    using var response = await http.PostAsync($"http://127.0.0.1:{httpPort}/playback/pause", null);
    response.EnsureSuccessStatusCode();
    await Until(() => Read("gui")?.Pauses == 1);
    Check("The shared control endpoint targets only the owner", Read("cli")!.Pauses == 0 && Read("tui")!.Pauses == 0);
    await Stop("gui", gui);
    await Until(() => Read("tui") is { Owner: true, Lyric: "shared words" } && Read("cli")?.Remote == "tui");
    Check("TUI takes precedence over CLI when GUI exits", Read("tui")!.Starts == 1 && Read("cli")!.Starts == 0);
    await Stop("tui", tui);
    await Until(() => Read("cli") is { Owner: true, Lyric: "shared words" });
    Check("CLI takes over after both higher-priority frontends leave", Read("cli")!.Starts == 1);
    var newerGui = Start("gui-new", ServiceRole.Gui);
    await Until(() => Read("gui-new")?.Remote == "cli");
    Check("A newly opened GUI reuses a healthy CLI owner", Read("gui-new")!.Starts == 0 && Read("cli")!.Owner);
    cli.Kill(true); await cli.WaitForExitAsync();
    await Until(() => Read("gui-new") is { Owner: true, Lyric: "shared words" });
    Check("A crashed owner releases its OS lease so another frontend recovers", Read("gui-new")!.Starts == 1);
    var peer1 = Start("peer1", ServiceRole.Tui); var peer2 = Start("peer2", ServiceRole.Tui);
    await Until(() => Read("peer1")?.Remote == "gui" && Read("peer2")?.Remote == "gui");
    await Stop("gui-new", newerGui);
    await Until(() => Read("peer1")!.Owner != Read("peer2")!.Owner && (Read("peer1")!.Remote == "tui" || Read("peer2")!.Remote == "tui"));
    Check("Equal-priority frontends elect exactly one owner", Read("peer1")!.Starts + Read("peer2")!.Starts == 1);
    await Stop("peer1", peer1); await Stop("peer2", peer2);
    using (var foreignTcp = new TcpListener(IPAddress.Loopback, httpPort))
    {
        foreignTcp.Start(); var contender = Start("foreign-http", ServiceRole.Gui);
        await Until(() => Read("foreign-http") != null); await Task.Delay(2500);
        Check("A foreign HTTP listener is left alone and no player backend starts", Read("foreign-http") is { Owner: false, Starts: 0 });
        foreignTcp.Stop();
        await Until(() => Read("foreign-http")!.Owner);
        Check("Waiting frontend recovers when the HTTP port becomes available", Read("foreign-http")!.Starts == 1);
        await Stop("foreign-http", contender);
    }
    using (var foreignUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, udpPort)))
    {
        var contender = Start("foreign-udp", ServiceRole.Gui);
        await Until(() => Read("foreign-udp") != null); await Task.Delay(1800);
        Check("A UDP conflict cannot leave a partially started owner", Read("foreign-udp") is { Owner: false, Starts: 0 });
        using var client = new LyricsServiceClient();
        Check("A failed dual-port claim leaves no HTTP service behind", await client.TryReadAsync(new Uri($"http://127.0.0.1:{httpPort}"), default) == null);
        foreignUdp.Dispose();
        await Until(() => Read("foreign-udp")!.Owner);
        Check("Waiting frontend recovers when the UDP port becomes available", Read("foreign-udp")!.Starts == 1);
        await Stop("foreign-udp", contender);
    }
    Console.WriteLine($"{passed} election checks passed");
}
finally
{
    foreach (var child in children) { if (!child.HasExited) child.Kill(true); child.Dispose(); }
    Directory.Delete(folderRoot, true);
}

sealed record Status(bool Owner, string? Remote, int Starts, int Loads, int Pauses, string? Lyric);
sealed class FakeBackend(Action start, Action pause) : BasePlayerBackend
{
    private readonly PlayerState _state = new() { Title = "Test song", Artists = ["Test artist"], SourceApp = "Mock", Duration = TimeSpan.FromMinutes(3), Playing = true };
    public override PlayerState GetCurrentState() => _state;
    public override Task StartAsync(CancellationToken token) { start(); return Task.CompletedTask; }
    public override Task PauseAsync() { pause(); _state.Playing = false; return Task.CompletedTask; }
    public override Task PlayAsync() => Task.CompletedTask;
    public override Task TogglePlayPauseAsync() => Task.CompletedTask;
    public override Task NextAsync() => Task.CompletedTask;
    public override Task PreviousAsync() => Task.CompletedTask;
    public override Task SeekAsync(TimeSpan pos) => Task.CompletedTask;
}
