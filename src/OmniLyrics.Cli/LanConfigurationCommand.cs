using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Network;

namespace OmniLyrics.Cli;

internal static class LanConfigurationCommand
{
    public static async Task RunAsync(string[] args)
    {
        var trust = new LanTrustStore();
        var settings = UserConfiguration.LoadLan();
        if (args.Length == 0)
        {
            if (Console.IsInputRedirected) throw new InvalidOperationException(Localization.Get("LanCliUsage"));
            Console.WriteLine(Localization.Get("LanMenu"));
            switch (Console.ReadLine()?.Trim())
            {
                case "1": await RunAsync([settings.Enabled ? "off" : "on"]); break;
                case "2": await RunAsync(["discover"]); break;
                case "3":
                    Console.WriteLine(Localization.Get("LanAddress"));
                    Console.WriteLine(string.Join("\n", LanAddress.LocalAddresses()));
                    var host = Console.ReadLine()?.Trim() ?? "";
                    Console.WriteLine(Localization.Get("LanControlPrompt"));
                    var control = Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
                    await RunAsync(control ? ["invite", host, "--control"] : ["invite", host]); break;
                case "4": await RunAsync(["pair"]); break;
                case "5": await RunAsync(["peers"]); Console.WriteLine(Localization.Get("LanSourcePrompt")); await RunAsync(["source", Console.ReadLine()?.Trim() ?? "local"]); break;
                case "6": await RunAsync(["peers"]); Console.WriteLine(Localization.Get("LanRevokePrompt")); await RunAsync(["revoke", Console.ReadLine()?.Trim() ?? ""]); break;
                case "7": await RunAsync(["cancel"]); break;
            }
            return;
        }
        switch (args)
        {
            case ["on"] or ["off"]:
                UserConfiguration.SaveLan(settings with { Enabled = args[0] == "on" });
                if (args[0] == "off") trust.CancelInvitation();
                break;
            case ["name", var name]: UserConfiguration.SaveLan(settings with { DeviceName = name }); break;
            case ["ports", var https, var udp]:
                UserConfiguration.SaveLan(settings with { HttpsPort = int.Parse(https), DiscoveryPort = int.Parse(udp) }); break;
            case ["discover"]:
                var found = await LanDiscovery.FindAsync(settings.DiscoveryPort);
                foreach (var device in found.Where(d => d.Id != trust.DeviceId)) Console.WriteLine($"{device.Id}  {device}");
                if (found.Count == 0) Console.WriteLine(Localization.Get("LanNoneFound"));
                return;
            case ["invite", _] or ["invite", _, "--control"]:
                Console.Error.WriteLine(Localization.Get("LanInviteHint"));
                Console.WriteLine(trust.CreateInvitation(args[1], settings, args.Length == 3));
                return;
            case ["cancel"]: trust.CancelInvitation(); break;
            case ["pair"] or ["pair", "--stdin"]:
                if (Console.IsInputRedirected && args.Length != 2) throw new InvalidOperationException(Localization.Get("LanCliUsage"));
                if (args.Length == 1) Console.Error.WriteLine(Localization.Get("LanPasteInvite"));
                var invitation = args.Length == 2 ? Console.ReadLine()?.Trim() ?? "" : ReadHidden();
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    var peer = await LanClient.PairAsync(invitation, LanTrustStore.Name(settings.DeviceName), trust, timeout.Token);
                    Console.WriteLine($"{Localization.Get("LanPaired")} {peer.Name} ({peer.Id})");
                }
                return;
            case ["source", var id]:
                if (id != "local" && !trust.Peers().Any(p => p.Id == id)) throw new InvalidOperationException(Localization.Get("LanPairFirst"));
                UserConfiguration.SaveLan(settings with { SelectedDeviceId = id == "local" ? null : id }); break;
            case ["peers"]:
                Console.WriteLine(Localization.Get("LanSource"));
                foreach (var p in trust.Peers()) Console.WriteLine($"{p.Id}  {p.Name}  {p.Host}  {Localization.Get(p.AllowControl ? "LanControl" : "LanReadOnly")}");
                Console.WriteLine(Localization.Get("LanAuthorized"));
                foreach (var p in trust.Grants()) Console.WriteLine($"{p.Id}  {p.Name}  {Localization.Get(p.AllowControl ? "LanControl" : "LanReadOnly")}");
                return;
            case ["revoke", var id]: trust.Revoke(id); break;
            case ["forget", var id]:
                trust.Forget(id);
                if (settings.SelectedDeviceId == id) UserConfiguration.SaveLan(settings with { SelectedDeviceId = null });
                break;
            default: throw new InvalidOperationException(Localization.Get("LanCliUsage"));
        }
        Console.WriteLine(Localization.Get("SettingsApplied"));
    }
    private static string ReadHidden()
    {
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString().Trim(); }
            if (key.Key == ConsoleKey.Escape) throw new OperationCanceledException();
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
            else if (!char.IsControl(key.KeyChar) && value.Length < 4096) value.Append(key.KeyChar);
        }
    }
}
