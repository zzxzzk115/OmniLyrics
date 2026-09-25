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
    private bool _disposed;
    internal Task Pending { get; private set; } = Task.CompletedTask;

    internal HyprlandBackdrop(Func<CancellationToken, string[], Task<string?>>? run = null) => _run = run ?? RunAsync;

    public void Apply(bool enabled, string title, bool force = false)
    {
        if (_disposed || !OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"))) return;
        if (!force && _requested == enabled) return;
        _requested = enabled;
        Pending = ApplyAsync(enabled, title, _lifetime.Token);
    }

    private async Task ApplyAsync(bool enabled, string title, CancellationToken token)
    {
        try
        {
            await _gate.WaitAsync(token);
            try
            {
                if (_requested != enabled) return;
                // The first map event can arrive just after Window.Opened.
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    if (_requested != enabled) return;
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
                        if (_requested != enabled) return;
                        // Hyprland 0.53 renamed these properties; try the current spelling first.
                        var result = await _run(token, ["setprop", "address:" + target, "no_blur", enabled ? "0" : "1"]);
                        if (result?.Trim() != "ok")
                            await _run(token, ["setprop", "address:" + target, "noblur", enabled ? "0" : "1"]);
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
    }

    private static async Task<string?> RunAsync(CancellationToken token, params string[] arguments)
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
