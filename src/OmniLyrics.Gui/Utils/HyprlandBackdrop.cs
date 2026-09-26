using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OmniLyrics.Gui.Utils;

/// <summary>Controls blur only on this process's lyric window; never changes compositor-wide settings.</summary>
internal sealed class HyprlandBackdrop : IDisposable
{
    private readonly Func<CancellationToken, string[], Task<string?>> _run;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1);
    private bool? _requested;
    private int _requestId;
    private int? _commandStyle;
    private bool _disposed;
    internal Task Pending { get; private set; } = Task.CompletedTask;

    internal HyprlandBackdrop(Func<CancellationToken, string[], Task<string?>>? run = null) => _run = run ?? RunAsync;

    public void Apply(bool enabled, string title, bool force = false)
    {
        if (_disposed || !OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"))) return;
        if (!force && _requested == enabled) return;
        _requested = enabled;
        Pending = ApplyAsync(enabled, title, ++_requestId, _lifetime.Token);
    }

    private async Task ApplyAsync(bool enabled, string title, int requestId, CancellationToken token)
    {
        var applied = false;
        try
        {
            await _gate.WaitAsync(token);
            try
            {
                if (_requestId != requestId) return;
                // The first map event can arrive just after Window.Opened.
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    if (_requestId != requestId) return;
                    var response = await _run(token, ["-j", "clients"]);
                    if (response == null) return;
                    using var clients = JsonDocument.Parse(response);
                    var window = clients.RootElement.EnumerateArray().FirstOrDefault(c =>
                        c.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out var processId) && processId == Environment.ProcessId
                        && c.TryGetProperty("title", out var name) && name.GetString() == title);
                    if (window.ValueKind == JsonValueKind.Object && window.TryGetProperty("address", out var address))
                    {
                        var target = address.GetString();
                        if (target == null || target.Length <= 2 || !target.StartsWith("0x", StringComparison.Ordinal)
                            || !target[2..].All(Uri.IsHexDigit)) return;
                        if (_requestId != requestId) return;
                        applied = await SetBlurAsync(token, target, enabled);
                        if (!applied) Debug.WriteLine("Hyprland backdrop unavailable: no supported window dispatcher.");
                        return;
                    }
                    await Task.Delay(150, token);
                }
            }
            finally { _gate.Release(); }
        }
        catch (Exception e) when (e is OperationCanceledException or JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A missing/stopped compositor leaves ordinary transparency available.
            Debug.WriteLine("Hyprland backdrop unavailable: " + e.GetType().Name);
        }
        finally
        {
            // A rejected request must not suppress a later retry of the same setting.
            if (!applied && _requestId == requestId) _requested = null;
        }
    }

    private async Task<bool> SetBlurAsync(CancellationToken token, string address, bool enabled)
    {
        var value = enabled ? "0" : "1";
        // 0.55+ Lua configs use a typed dispatcher; 0.53/0.54 use the hyprlang
        // dispatcher. Older releases expose setprop directly with either spelling.
        // Only the validated hexadecimal window address is inserted into Lua.
        string[][] commands = [
            ["dispatch", $"hl.dsp.window.set_prop({{ window = \"address:{address}\", prop = \"no_blur\", value = \"{value}\" }})"],
            ["dispatch", "setprop", "address:" + address, "no_blur", value],
            ["setprop", "address:" + address, "no_blur", value],
            ["setprop", "address:" + address, "noblur", value]
        ];
        var preferred = _commandStyle;
        var order = Enumerable.Range(0, commands.Length).OrderBy(style => style == preferred ? 0 : 1);
        foreach (var style in order)
        {
            token.ThrowIfCancellationRequested();
            if ((await _run(token, commands[style]))?.Trim() != "ok") continue;
            _commandStyle = style;
            return true;
        }
        _commandStyle = null;
        return false;
    }

    internal static async Task<string?> RunAsync(CancellationToken token, params string[] arguments)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var start = new ProcessStartInfo("hyprctl")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        if (process == null) return null;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await error;
            var text = await output;
            return process.ExitCode == 0 ? text : null;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
