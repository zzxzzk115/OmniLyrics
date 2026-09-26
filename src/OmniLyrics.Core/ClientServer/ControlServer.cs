using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OmniLyrics.Core;

public class CommandServer : IDisposable
{
    private readonly IPlayerBackend _backend;
    private readonly UdpClient _udp;

    public CommandServer(IPlayerBackend backend, string listenAddress = "127.0.0.1", int port = ClientServerCommonDefine.ControlPort)
    {
        _backend = backend;
        var address = IPAddress.Parse(listenAddress);
        var endpoint = new IPEndPoint(IPAddress.IsLoopback(address) ? address : IPAddress.Loopback, port);
        _udp = new UdpClient(endpoint.AddressFamily);
        try
        {
            _udp.ExclusiveAddressUse = true;
            _udp.Client.Bind(endpoint);
        }
        catch { _udp.Dispose(); throw; }
    }

    public async Task StartAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var result = await _udp.ReceiveAsync(token);
            if (!IPAddress.IsLoopback(result.RemoteEndPoint.Address) || result.Buffer.Length > 128) continue;
            string cmd = Encoding.UTF8.GetString(result.Buffer);
            _ = HandleCommandAsync(cmd);
        }
    }

    public void Dispose() => _udp.Dispose();

    private Task HandleCommandAsync(string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd))
            return Task.CompletedTask;

        if (cmd == "play") return _backend.PlayAsync();
        if (cmd == "pause") return _backend.PauseAsync();
        if (cmd == "toggle") return _backend.TogglePlayPauseAsync();
        if (cmd == "next") return _backend.NextAsync();
        if (cmd == "prev") return _backend.PreviousAsync();

        if (cmd.StartsWith("seek "))
        {
            if (double.TryParse(cmd.Substring(5), out double sec) && double.IsFinite(sec) && sec >= 0 && sec < TimeSpan.MaxValue.TotalSeconds / 2)
                return _backend.SeekAsync(TimeSpan.FromSeconds(sec));
        }

        return Task.CompletedTask;
    }
}
