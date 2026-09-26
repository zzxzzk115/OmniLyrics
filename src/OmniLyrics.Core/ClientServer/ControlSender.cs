using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OmniLyrics.Core;

public static class ControlSender
{
    public static async Task SendAsync(string cmd, string host = ClientServerCommonDefine.Host, int port = ClientServerCommonDefine.ControlPort)
    {
        var addresses = await Dns.GetHostAddressesAsync(host);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6)
            ?? throw new SocketException((int)SocketError.HostNotFound);
        if (!IPAddress.IsLoopback(address)) throw new InvalidOperationException(Localization.Get("LanUsePairing"));
        using var udp = new UdpClient(address.AddressFamily);
        byte[] data = Encoding.UTF8.GetBytes(cmd);
        await udp.SendAsync(data, new IPEndPoint(address, port));
    }
}
