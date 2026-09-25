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
var legacy = false;
using var backdrop = new HyprlandBackdrop((_, args) =>
{
    commands.Add(args);
    return Task.FromResult<string?>(args[0] == "-j" ? clients : legacy && args[2] == "no_blur" ? "unknown prop" : "ok\n");
});
void Check(string name, bool value)
{
    if (!value) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
backdrop.Apply(true, title); await backdrop.Pending;
Check("Blur changes target only the matching process and lyric window",
    commands.Count == 2 && commands[1].SequenceEqual(new[] { "setprop", "address:" + address, "no_blur", "0" }));
backdrop.Apply(true, title); await backdrop.Pending;
Check("Unchanged settings do not repeatedly invoke the compositor", commands.Count == 2);
backdrop.Apply(false, title); await backdrop.Pending;
Check("Disabling blur targets the same window", commands.Last().Last() == "1");
legacy = true; commands.Clear();
backdrop.Apply(true, title); await backdrop.Pending;
Check("Older compositor property spelling is supported without global rules",
    commands.Count == 3 && commands[2].SequenceEqual(new[] { "setprop", "address:" + address, "noblur", "0" }));
commands.Clear(); clients = "invalid json";
backdrop.Apply(false, title); await backdrop.Pending;
Check("Malformed IPC responses leave the compositor unchanged", commands.Count == 1);
commands.Clear(); clients = JsonSerializer.Serialize(new[] { new { pid = Environment.ProcessId, title, address = "0x;bad" } });
backdrop.Apply(true, title); await backdrop.Pending;
Check("Invalid window addresses cannot become commands", commands.Count == 1);
commands.Clear(); backdrop.Dispose(); backdrop.Apply(false, title); await backdrop.Pending;
Check("A closed window never sends further commands", commands.Count == 0);
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
Console.WriteLine("13 backdrop and monitor checks passed");
