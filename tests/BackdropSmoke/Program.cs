using System.Text.Json;
using OmniLyrics.Gui.Utils;

// Inject the IPC transport: this test must never contact a real compositor.
if (!OperatingSystem.IsLinux()) return;
Environment.SetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE", "mock-compositor");
var commands = new List<string[]>();
var title = "OmniLyrics · Lyrics";
var address = "0x1234ab";
var clients = JsonSerializer.Serialize(new[] {
    new { pid = Environment.ProcessId + 1, title, address = "0xbad" },
    new { pid = Environment.ProcessId, title = "Settings", address = "0xbad2" },
    new { pid = Environment.ProcessId, title, address }
});
var transport = "lua";
string Lua(string value) => $"hl.dsp.window.set_prop({{ window = \"address:{address}\", prop = \"no_blur\", value = \"{value}\" }})";
string[] Request(string style, string value) => style switch
{
    "lua" => ["dispatch", Lua(value)],
    "dispatcher" => ["dispatch", "setprop", "address:" + address, "no_blur", value],
    "direct" => ["setprop", "address:" + address, "no_blur", value],
    _ => ["setprop", "address:" + address, "noblur", value]
};
using var backdrop = new HyprlandBackdrop((_, args) =>
{
    commands.Add(args);
    return Task.FromResult<string?>(args[0] == "-j" ? clients
        : transport != "unavailable" && new[] { "0", "1" }.Any(value => args.SequenceEqual(Request(transport, value)))
            ? "ok\n" : "unknown request");
});
var passed = 0;
void Check(string name, bool value)
{
    if (!value) throw new Exception(name);
    passed++;
    Console.WriteLine("PASS " + name);
}
backdrop.Apply(true, title); await backdrop.Pending;
Check("Lua blur dispatch targets only the matching process and lyric window",
    commands.Count == 2 && commands[1].SequenceEqual(Request("lua", "0")));
backdrop.Apply(true, title); await backdrop.Pending;
Check("Unchanged settings do not repeatedly invoke the compositor", commands.Count == 2);
backdrop.Apply(false, title); await backdrop.Pending;
Check("Disabling blur uses the same Lua window dispatcher", commands.Last().SequenceEqual(Request("lua", "1")));
transport = "dispatcher"; commands.Clear();
backdrop.Apply(true, title); await backdrop.Pending;
Check("Hyprlang configurations fall back to the setprop dispatcher",
    commands.Count == 3 && commands.Last().SequenceEqual(Request("dispatcher", "0")));
commands.Clear(); backdrop.Apply(false, title); await backdrop.Pending;
Check("The working transport is reused without failed probes", commands.Count == 2 && commands.Last().SequenceEqual(Request("dispatcher", "1")));
transport = "direct"; commands.Clear();
backdrop.Apply(true, title); await backdrop.Pending;
Check("Direct setprop with the renamed property remains supported", commands.Last().SequenceEqual(Request("direct", "0")));
transport = "legacy"; commands.Clear();
backdrop.Apply(false, title); await backdrop.Pending;
Check("Older property spelling is supported without global rules", commands.Last().SequenceEqual(Request("legacy", "1")));
Check("All compatibility probes are scoped to the validated lyric window", commands.Skip(1).All(command =>
    new[] { "lua", "dispatcher", "direct", "legacy" }.Any(style => command.SequenceEqual(Request(style, "1")))));
transport = "unavailable"; commands.Clear();
backdrop.Apply(true, title); await backdrop.Pending;
Check("Unsupported transports are tried once with no unbounded retry", commands.Count == 5);
transport = "lua"; commands.Clear();
backdrop.Apply(true, title); await backdrop.Pending;
Check("A failed request does not suppress retrying the same blur setting", commands.Count == 2 && commands.Last().SequenceEqual(Request("lua", "0")));
commands.Clear(); var validClients = clients; clients = "invalid json";
backdrop.Apply(false, title); await backdrop.Pending;
Check("Malformed IPC responses leave the compositor unchanged", commands.Count == 1);
commands.Clear(); clients = validClients;
backdrop.Apply(false, title); await backdrop.Pending;
Check("The same request recovers after a malformed response", commands.Count == 2 && commands.Last().SequenceEqual(Request("lua", "1")));
foreach (var invalidAddress in new[] { "0x;bad", "0x1234\"}); print('bad')", "0x", "1234" })
{
    commands.Clear(); clients = JsonSerializer.Serialize(new[] { new { pid = Environment.ProcessId, title, address = invalidAddress } });
    backdrop.Apply(true, title); await backdrop.Pending;
    Check("Invalid window address cannot become Lua or legacy commands: " + invalidAddress, commands.Count == 1);
}
commands.Clear(); clients = JsonSerializer.Serialize(new[] { new { pid = Environment.ProcessId + 1, title, address } });
backdrop.Apply(true, title); await backdrop.Pending;
Check("Another process's window is never modified", commands.Count == 4 && commands.All(command => command.SequenceEqual(new[] { "-j", "clients" })));
commands.Clear(); backdrop.Dispose(); backdrop.Apply(false, title); await backdrop.Pending;
Check("A closed window never sends further commands", commands.Count == 0);
var firstQuery = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
var queried = false;
using (var rapid = new HyprlandBackdrop((_, args) =>
{
    commands.Add(args);
    if (args[0] != "-j") return Task.FromResult<string?>("ok");
    if (queried) return Task.FromResult<string?>(validClients);
    queried = true;
    return firstQuery.Task;
}))
{
    rapid.Apply(true, title); var earlier = rapid.Pending;
    rapid.Apply(false, title);
    firstQuery.SetResult(validClients);
    await Task.WhenAll(earlier, rapid.Pending);
    Check("A newer toggle supersedes a pending window lookup", commands.Count == 3
        && commands.Last().SequenceEqual(Request("lua", "1")));
}
var monitors = "[{\"id\":0,\"scale\":1.6666667},{\"id\":1,\"scale\":2}]";
var scaleClients = JsonSerializer.Serialize(new[] {
    new { pid = Environment.ProcessId, title, monitor = 0, xwayland = true },
    new { pid = Environment.ProcessId + 1, title = "Unrelated", monitor = 1, xwayland = true }
});
var snapshot = HyprlandScaleSnapshot.Parse(monitors, scaleClients, "{\"bool\":true}", Environment.ProcessId);
Check("Follow-system uses compositor scale instead of stale Xft DPI", Math.Abs(snapshot.Factor(title, 2, null)!.Value * 2 - 1.6666667) < .00001);
Check("Absolute manual scaling does not multiply system DPI", snapshot.Factor(title, 2, 1.5) == .75);
Check("Unrelated windows cannot select this application's monitor", snapshot.Factor("Unrelated", 2, null) == null);
var composited = HyprlandScaleSnapshot.Parse(monitors, scaleClients, "{\"int\":0}", Environment.ProcessId);
Check("Compositor-scaled XWayland does not scale twice", Math.Abs(composited.Factor(title, 2, null)!.Value - .5) < .00001);
Check("Manual override accounts for compositor-side scaling", Math.Abs(composited.Factor(title, 2, 1.5)!.Value * 2 * 1.6666667 - 1.5) < .00001);
var moved = HyprlandScaleSnapshot.Parse(monitors, scaleClients.Replace("\"monitor\":0", "\"monitor\":1"), "{\"bool\":true}", Environment.ProcessId);
Check("Moving to another monitor picks up its scaling", moved.Factor(title, 2, null) == 1);
Console.WriteLine($"{passed} backdrop and monitor checks passed");
