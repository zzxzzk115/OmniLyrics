using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Core.Network;

public sealed record DiscoveredDevice(string Id, string Name, string Host, int Port)
{
    public override string ToString() => $"{Name} · {Host}:{Port}";
}

/// <summary>IPv4 discovery carries identity hints only; it never establishes trust or carries player data.</summary>
public sealed class LanDiscovery : IDisposable
{
    private sealed record Probe(string Service, int Version, string Nonce);
    private sealed record Reply(string Service, int Version, string Nonce, string Id, string Name, int Port);
    private const string Service = "OmniLyrics.Discovery";
    private static readonly JsonSerializerOptions WireJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly UdpClient _udp;
    private readonly string _id, _name;
    private readonly int _port;
    public LanDiscovery(LanSettings settings, string id, IPAddress? bindAddress = null)
    {
        _id = id; _name = LanTrustStore.Name(settings.DeviceName); _port = settings.HttpsPort;
        _udp = new UdpClient(AddressFamily.InterNetwork);
        try { _udp.ExclusiveAddressUse = true; _udp.Client.Bind(new IPEndPoint(bindAddress ?? IPAddress.Any, settings.DiscoveryPort)); }
        catch { _udp.Dispose(); throw; }
    }
    public async Task RunAsync(CancellationToken token)
    {
        long window = 0; int count = 0;
        while (!token.IsCancellationRequested)
        {
            var packet = await _udp.ReceiveAsync(token);
            if (packet.Buffer.Length > 512 || !LanAddress.IsPrivate(packet.RemoteEndPoint.Address)) continue;
            var now = Environment.TickCount64;
            if (now - window >= 1000) { window = now; count = 0; }
            if (++count > 10) continue; // Bound amplification and parsing cost, including spoofed-source traffic.
            try
            {
                var probe = JsonSerializer.Deserialize<Probe>(packet.Buffer);
                if (probe?.Service != Service || probe.Version != 1 || probe.Nonce is not { Length: 32 } || !probe.Nonce.All(char.IsAsciiHexDigit)) continue;
                var reply = JsonSerializer.SerializeToUtf8Bytes(new Reply(Service, 1, probe.Nonce, _id, _name, _port), WireJson);
                await _udp.SendAsync(reply, packet.RemoteEndPoint, token);
            }
            catch (JsonException) { }
        }
    }
    public static async Task<IReadOnlyList<DiscoveredDevice>> FindAsync(int port = 32652, CancellationToken token = default,
        IPAddress? destination = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var probe = JsonSerializer.SerializeToUtf8Bytes(new Probe(Service, 1, nonce));
        var destinations = destination != null ? new[] { destination } : BroadcastAddresses();
        foreach (var target in destinations)
            try { await udp.SendAsync(probe, new IPEndPoint(target, port), token); }
            catch (SocketException) when (destination == null) { }

        var devices = new Dictionary<string, DiscoveredDevice>();
        try
        {
            while (devices.Count < 64)
            {
                var packet = await udp.ReceiveAsync(timeout.Token);
                if (packet.Buffer.Length > 512 || !LanAddress.IsPrivate(packet.RemoteEndPoint.Address)) continue;
                try
                {
                    var reply = JsonSerializer.Deserialize<Reply>(packet.Buffer);
                    if (reply?.Service != Service || reply.Version != 1 || reply.Nonce != nonce
                        || !Guid.TryParseExact(reply.Id, "N", out _) || reply.Name is not { Length: > 0 and <= 64 } || reply.Name.Any(char.IsControl)
                        || reply.Port is < 1024 or > 65535) continue;
                    devices[reply.Id] = new(reply.Id, reply.Name, packet.RemoteEndPoint.Address.ToString(), reply.Port);
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        return devices.Values.ToArray();
    }
    private static IPAddress[] BroadcastAddresses() => System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && LanAddress.IsPrivate(a.Address))
        .Select(a => new IPAddress(a.Address.GetAddressBytes().Zip(a.IPv4Mask.GetAddressBytes(), (b, mask) => (byte)(b | ~mask)).ToArray()))
        .Append(IPAddress.Broadcast).Distinct().Take(16).ToArray();
    public void Dispose() => _udp.Dispose();
}
