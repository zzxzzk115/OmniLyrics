using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Network;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Shared;
using OmniLyrics.Web;

var passed = 0;
void Check(string name, bool ok) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
async Task Until(Func<bool> predicate)
{ for (int i = 0; i < 150; i++) { if (predicate()) return; await Task.Delay(50); } throw new Exception("Condition timed out"); }
int TcpPort() { var s = new TcpListener(IPAddress.Loopback, 0); s.Start(); var p = ((IPEndPoint)s.LocalEndpoint).Port; s.Stop(); return p; }
int UdpPort() { using var s = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); return ((IPEndPoint)s.Client.LocalEndPoint!).Port; }
var root = Path.Combine(Path.GetTempPath(), "omnilyrics-lan-" + Guid.NewGuid().ToString("N"));
var previous = Environment.GetEnvironmentVariable("OMNILYRICS_CONFIG_DIR");
Environment.SetEnvironmentVariable("OMNILYRICS_CONFIG_DIR", Path.Combine(root, "client"));
try
{
    var store = new LanTrustStore(Path.Combine(root, "server"));
    var clientStore = new LanTrustStore();
    var settings = new LanSettings(true, "Test server", TcpPort(), UdpPort());
    Check("LAN sharing defaults off", !UserConfiguration.LoadLan().Enabled);
    using var certificate = store.Certificate();
    var fingerprint = LanTrustStore.Fingerprint(certificate);
    using var reloaded = new LanTrustStore(Path.Combine(root, "server")).Certificate();
    Check("Identity survives service ownership changes", fingerprint == LanTrustStore.Fingerprint(reloaded));
    if (!OperatingSystem.IsWindows()) Check("Trust file is private", (File.GetUnixFileMode(Path.Combine(root, "server/lan-trust.json")) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0);
    var backend = new FakeBackend("Remote song");
    var manager = new LyricsManager((s, k) => Task.FromResult<List<LyricsLine>?>([new(TimeSpan.Zero, s.Title!, [new(TimeSpan.Zero, TimeSpan.FromSeconds(2), s.Title!)], new() { ["zh"] = "译文" })]));
    var localPort = TcpPort();
    await using var server = new WebApiServer(backend, new EmptyLyrics(), localPort, "0.0.0.0", manager,
        lan: settings, trust: store, lanBindAddress: IPAddress.Loopback);
    await server.StartAsync(default);
    Check("Legacy wildcard HTTP configuration binds only to loopback", System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
        .GetActiveTcpListeners().Where(e => e.Port == localPort).All(e => IPAddress.IsLoopback(e.Address)));
    var remoteUdpRejected = false;
    try { await ControlSender.SendAsync("pause", "192.0.2.1"); } catch (InvalidOperationException) { remoteUdpRejected = true; }
    Check("Legacy remote UDP control cannot bypass pairing", remoteUdpRejected);
    using var local = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{localPort}/") };
    Check("Same-machine snapshot requires no authentication", (await local.GetAsync("snapshot")).IsSuccessStatusCode);
    Check("Same-machine playback remains authentication-free", (await local.PostAsync("playback/pause", null)).IsSuccessStatusCode && backend.Pauses == 1);
    using (var request = new HttpRequestMessage(HttpMethod.Post, "playback/pause"))
    {
        request.Headers.Add("Origin", "https://untrusted.example");
        Check("Browser CSRF cannot control local playback", (await local.SendAsync(request)).StatusCode == HttpStatusCode.Forbidden && backend.Pauses == 1);
    }
    using (var request = new HttpRequestMessage(HttpMethod.Get, "snapshot"))
    {
        request.Headers.Host = "untrusted.example";
        Check("Local Host validation blocks DNS rebinding", (await local.SendAsync(request)).StatusCode == HttpStatusCode.Forbidden);
    }
    Check("Pairing is not exposed on plain HTTP", (await local.PostAsJsonAsync("lan/pair", new PairingRequest("", "x"))).StatusCode == HttpStatusCode.NotFound);
    HttpClient Secure(string? token = null)
    {
        var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, c, _, _) => c != null && c.GetCertHashString(HashAlgorithmName.SHA256) == fingerprint })
            { BaseAddress = new Uri($"https://127.0.0.1:{settings.HttpsPort}/"), Timeout = TimeSpan.FromSeconds(3) };
        if (token != null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
    using var anonymous = Secure();
    foreach (var path in new[] { "snapshot", "lyrics", "playback/state", "favorites" })
        Check("Unpaired HTTPS read rejected: " + path, (await anonymous.GetAsync(path)).StatusCode == HttpStatusCode.Unauthorized);
    foreach (var path in new[] { "playback/play", "playback/pause", "playback/toggle", "playback/prev", "playback/next", "playback/seek", "favorites" })
        Check("Unpaired HTTPS write rejected: " + path, (await anonymous.PostAsJsonAsync(path, new { position = 1 })).StatusCode == HttpStatusCode.Unauthorized);
    using (var malformed = new UdpClient())
        await malformed.SendAsync("{\"Service\":\"OmniLyrics.Discovery\",\"Version\":1,\"Nonce\":null}"u8.ToArray(), new IPEndPoint(IPAddress.Loopback, settings.DiscoveryPort));
    var found = await LanDiscovery.FindAsync(settings.DiscoveryPort, destination: IPAddress.Loopback);
    Check("UDP discovery survives malformed packets and returns only identity hints", found.Count == 1 && found[0].Id == store.DeviceId && found[0].Name == settings.DeviceName && found[0].Port == settings.HttpsPort);
    var invitation = store.CreateInvitation("127.0.0.1", settings);
    var peer = await LanClient.PairAsync(invitation, "Reader", clientStore);
    Check("Pairing defaults to read-only", !peer.AllowControl && store.Grants().Count == 1);
    Check("Server stores only a token hash", !File.ReadAllText(Path.Combine(root, "server/lan-trust.json")).Contains(peer.Token));
    using var readOnly = Secure(peer.Token);
    using var paired = new LanClient(peer);
    await Until(() => manager.Capture(backend.State).Lines?.Count > 0);
    var snapshot = await paired.ReadAsync(default);
    Check("Paired device receives karaoke and bilingual lyrics", snapshot?.State?.Title == "Remote song" && snapshot.Lyrics?[0].Tokens?[0].Duration == TimeSpan.FromSeconds(2) && snapshot.Lyrics[0].Translation == "译文");
    foreach (var path in new[] { "playback/play", "playback/pause", "playback/toggle", "playback/prev", "playback/next", "playback/seek", "favorites" })
        Check("Read-only grant cannot write: " + path, (await readOnly.PostAsJsonAsync(path, new { position = 1 })).StatusCode == HttpStatusCode.Forbidden);
    Check("Read-only grant does not expose favorites", (await readOnly.GetAsync("favorites")).StatusCode == HttpStatusCode.Forbidden);
    var used = LanTrustStore.ParseInvitation(invitation);
    Check("Invitation cannot be replayed", (await anonymous.PostAsJsonAsync("lan/pair", new PairingRequest(used.Secret, "Replay"))).StatusCode == HttpStatusCode.Unauthorized);
    using (var wrongPin = new LanClient(peer with { Fingerprint = new string('0', 64) }))
        Check("Wrong certificate is rejected without plaintext fallback", await wrongPin.ReadAsync(default) == null);
    var freshInvitation = store.CreateInvitation("127.0.0.1", settings);
    var substituted = LanTrustStore.ParseInvitation(freshInvitation) with { Fingerprint = new string('0', 64) };
    var forgedInvitation = "omnilyrics://pair#" + Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(substituted)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var beforePinFailure = store.Grants().Count;
    try { await LanClient.PairAsync(forgedInvitation, "Pin failure", clientStore); } catch (HttpRequestException) { }
    var pinPeer = await LanClient.PairAsync(freshInvitation, "Pin verified", new LanTrustStore(Path.Combine(root, "pin-client")));
    Check("Pairing verifies the certificate before consuming the invitation", store.Grants().Count == beforePinFailure + 1 && store.Grants().All(g => g.Name != "Pin failure"));
    using (var oversized = new StringContent(System.Text.Json.JsonSerializer.Serialize(new PairingRequest(new string('0', 64), new string('a', 9000))), System.Text.Encoding.UTF8, "application/json"))
        Check("Oversized pairing bodies are rejected", (await anonymous.PostAsync("lan/pair", oversized)).StatusCode == HttpStatusCode.RequestEntityTooLarge);
    var expired = LanTrustStore.ParseInvitation(store.CreateInvitation("127.0.0.1", settings));
    var trustFile = Path.Combine(root, "server/lan-trust.json");
    var document = JsonNode.Parse(File.ReadAllText(trustFile))!;
    document["Invitation"]!["Expires"] = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O");
    File.WriteAllText(trustFile, document.ToJsonString());
    Check("Expired invitations cannot grant access", (await anonymous.PostAsJsonAsync("lan/pair", new PairingRequest(expired.Secret, "Expired"))).StatusCode == HttpStatusCode.Unauthorized);
    var cancelled = LanTrustStore.ParseInvitation(store.CreateInvitation("127.0.0.1", settings)); store.CancelInvitation();
    Check("Cancelled invitations cannot grant access", (await anonymous.PostAsJsonAsync("lan/pair", new PairingRequest(cancelled.Secret, "Cancelled"))).StatusCode == HttpStatusCode.Unauthorized);
    var controlPeer = await LanClient.PairAsync(store.CreateInvitation("127.0.0.1", settings, true), "Controller", new LanTrustStore(Path.Combine(root, "controller")));
    using var controller = Secure(controlPeer.Token);
    Check("Explicit control grant reaches the player", (await controller.PostAsync("playback/next", null)).IsSuccessStatusCode && backend.Nexts == 1);
    Check("Invalid seek is rejected", (await controller.PostAsJsonAsync("playback/seek", new { position = -1 })).StatusCode == HttpStatusCode.BadRequest);
    var grant = store.Grants().Single(g => g.Name == "Controller"); store.Revoke(grant.Id);
    Check("Revocation is immediate on an existing connection", (await controller.PostAsync("playback/next", null)).StatusCode == HttpStatusCode.Unauthorized && backend.Nexts == 1);

    UserConfiguration.SaveLan(new(false, "Client", DiscoveryPort: UdpPort(), SelectedDeviceId: peer.Id));
    var createdForCommand = false;
    var rejectedCommand = false;
    try { await LyricsCliRunner.RunAsync(() => { createdForCommand = true; return new FakeBackend("Wrong source"); }, ["--control", "next"]); }
    catch (InvalidOperationException) { rejectedCommand = true; }
    Check("CLI control respects the read-only remote source without starting a local backend", rejectedCommand && !createdForCommand && backend.Nexts == 1);
    var ipcPort = TcpPort(); var udpPort = UdpPort();
    var localBackend = new FakeBackend("Local song");
    var localManager = new LyricsManager((s, k) => Task.FromResult<List<LyricsLine>?>([new(TimeSpan.Zero, s.Title!, null)]));
    await using (var session = new SharedPlayerSession(() => localBackend, ServiceRole.Gui,
        () => new("127.0.0.1", ipcPort, udpPort, "127.0.0.1"), lyrics: localManager))
    {
        await session.StartAsync(default);
        await Until(() => session.OwnsService && session.GetCurrentState()?.Title == "Remote song");
        Check("Remote source does not preempt the local service", localBackend.Started && !session.CanControl);
        using var ipc = new HttpClient();
        var exported = await ipc.GetFromJsonAsync<LyricsSnapshot>($"http://127.0.0.1:{ipcPort}/snapshot");
        Check("Local export never relays the selected remote source", exported?.State?.Title == "Local song");
        await session.NextAsync();
        Check("Read-only source cannot fall back to controlling the local player", localBackend.Nexts == 0 && backend.Nexts == 1);
        store.Revoke(store.Grants().Single(g => g.Name == "Reader").Id);
        await Until(() => session.GetCurrentState() == null);
        Check("Revoked source clears stale lyrics and remains offline", session.CaptureLyrics(null).Lines == null && session.ServiceError != null && session.OwnsService);
        UserConfiguration.SaveLan(UserConfiguration.LoadLan() with { SelectedDeviceId = null });
        await Until(() => session.GetCurrentState()?.Title == "Local song");
        Check("Switching back restores local playback without a restart", session.CanControl && session.OwnsService);
    }
    var blocked = new TcpListener(IPAddress.Any, TcpPort()); blocked.Start();
    var blockedPort = ((IPEndPoint)blocked.LocalEndpoint).Port;
    UserConfiguration.SaveLan(new(true, "Local failure test", blockedPort, UdpPort()));
    var safePort = TcpPort();
    var safeUdp = UdpPort();
    await using (var session = new SharedPlayerSession(() => new FakeBackend("Still local"), ServiceRole.Cli,
        () => new("127.0.0.1", safePort, safeUdp, "127.0.0.1"), lyrics: localManager))
    {
        await session.StartAsync(default);
        await Until(() => session.OwnsService);
        using var http = new HttpClient();
        LanSharingStatus? status = null;
        for (var i = 0; i < 50; i++)
        {
            status = await http.GetFromJsonAsync<LanSharingStatus>($"http://127.0.0.1:{safePort}/sharing");
            if (status?.Error != null) break;
            await Task.Delay(100);
        }
        Check("LAN bind failure reports status without breaking local IPC", status is { Enabled: true, Running: false, Error: not null }
            && session.OwnsService && session.GetCurrentState()?.Title == "Still local"
            && (await http.GetAsync($"http://127.0.0.1:{safePort}/snapshot")).IsSuccessStatusCode);
    }
    blocked.Stop();
    UserConfiguration.SaveLan(new());
    var raceInvite = LanTrustStore.ParseInvitation(store.CreateInvitation("127.0.0.1", settings));
    var race = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => anonymous.PostAsJsonAsync("lan/pair", new PairingRequest(raceInvite.Secret, "Race"))));
    Check("Concurrent redemption grants access exactly once", race.Count(r => r.IsSuccessStatusCode) == 1);
    foreach (var response in race) response.Dispose();
    HttpStatusCode code = 0;
    for (var i = 0; i < 14; i++) { using var response = await anonymous.PostAsJsonAsync("lan/pair", new PairingRequest(new string('0', 64), "Guess")); code = response.StatusCode; }
    Check("Pairing guesses are rate limited", code == HttpStatusCode.TooManyRequests);
    Console.WriteLine($"{passed} LAN security checks passed");
}
finally { Environment.SetEnvironmentVariable("OMNILYRICS_CONFIG_DIR", previous); Directory.Delete(root, true); }
sealed class EmptyLyrics : ILyricsProvider { public List<LyricsLine>? CurrentLyrics => null; }
sealed class FakeBackend(string title) : BasePlayerBackend, IDisposable
{
    public PlayerState State = new() { Title = title, Artists = ["Test artist"], Duration = TimeSpan.FromMinutes(3), Playing = true };
    public int Pauses, Nexts; public bool Started;
    public override PlayerState? GetCurrentState() => State;
    public override Task StartAsync(CancellationToken token) { Started = true; return Task.CompletedTask; }
    public override Task PlayAsync() => Task.CompletedTask;
    public override Task PauseAsync() { Pauses++; return Task.CompletedTask; }
    public override Task TogglePlayPauseAsync() => Task.CompletedTask;
    public override Task NextAsync() { Nexts++; return Task.CompletedTask; }
    public override Task PreviousAsync() => Task.CompletedTask;
    public override Task SeekAsync(TimeSpan position) => Task.CompletedTask;
    public void Dispose() { }
}
