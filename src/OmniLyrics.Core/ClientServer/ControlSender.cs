using System.Net.Sockets;
using System.Text;

namespace OmniLyrics.Core;

public static class ControlSender
{
    public static async Task SendAsync(string cmd)
    {
        using var udp = new UdpClient();
        byte[] data = Encoding.UTF8.GetBytes(cmd);
        await udp.SendAsync(data, data.Length, ClientServerCommonDefine.Host, ClientServerCommonDefine.ControlPort);
    }
}