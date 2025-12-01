using System.Net.Sockets;
using System.Text;

namespace OmniLyrics.Core;

public class CommandServer
{
    private readonly IPlayerBackend _backend;

    public CommandServer(IPlayerBackend backend)
    {
        _backend = backend;
    }

    public async Task StartAsync(CancellationToken token)
    {
        using var udp = new UdpClient(ClientServerCommonDefine.ControlPort);

        while (!token.IsCancellationRequested)
        {
            var result = await udp.ReceiveAsync(token);
            string cmd = Encoding.UTF8.GetString(result.Buffer);
            _ = HandleCommandAsync(cmd);
        }
    }

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
            if (double.TryParse(cmd.Substring(5), out double sec))
                return _backend.SeekAsync(TimeSpan.FromSeconds(sec));
        }

        return Task.CompletedTask;
    }
}