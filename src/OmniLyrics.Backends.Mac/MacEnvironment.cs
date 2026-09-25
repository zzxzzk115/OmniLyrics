using System.Diagnostics;
using System.Text.Json;
using OmniLyrics.Core;

namespace OmniLyrics.Backends.Mac;

public sealed record MacEnvironmentStatus(bool? NativeAvailable, bool MediaControlAvailable, string? MediaControlPath, string? BrewPath, bool MediaControlDisabled = false)
{
    // null means an authorization request may still be pending, not a failed bridge.
    public bool NeedsInstallation => NativeAvailable == false && !MediaControlAvailable && !MediaControlDisabled;
}

/// <summary>Read-only checks are separate from an explicitly requested installation.</summary>
public static class MacEnvironment
{
    private const string ProbeScript = """
        ObjC.import('AppKit');
        function run() {
            var ids = ['com.apple.Music', 'com.spotify.client'];
            var failed = false, pending = false;
            for (var i = 0; i < ids.length; i++) {
                if (!($.NSRunningApplication.runningApplicationsWithBundleIdentifier(ids[i]).count > 0)) continue;
                try { Application(ids[i]).playerState(); }
                catch (e) {
                    var code = Number(e.errorNumber || e.number || 0);
                    if (code === -1712) pending = true;
                    else failed = true;
                }
            }
            return JSON.stringify({available: pending ? null : !failed});
        }
        """;

    internal static string? FindTool(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Concat(new[] { "/opt/homebrew/bin", "/usr/local/bin" })
        .Where(directory => !string.IsNullOrWhiteSpace(directory))
        .Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists);

    public static async Task<MacEnvironmentStatus> ProbeAsync(CancellationToken token = default)
    {
        if (!OperatingSystem.IsMacOS()) return new(null, false, null, null);
        var native = ProbeNativeAsync(token);
        var disabled = string.Equals(Environment.GetEnvironmentVariable("OMNILYRICS_MEDIA_CONTROL"), "off", StringComparison.OrdinalIgnoreCase);
        var path = disabled ? FindTool("media-control") : MacOSMediaControlBackend.ResolveExecutable();
        var fallback = CheckMediaControlAsync(path, token);
        await Task.WhenAll(native, fallback).ConfigureAwait(false);
        return new(await native, !disabled && await fallback, path, FindTool("brew"), disabled);
    }

    private static async Task<bool?> ProbeNativeAsync(CancellationToken token)
    {
        if (!File.Exists("/usr/bin/osascript")) return false;
        try
        {
            var json = await MacProcess.CaptureAsync(MacProcess.StartInfo("/usr/bin/osascript", "-l", "JavaScript", "-e", ProbeScript), token, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var available = document.RootElement.GetProperty("available");
            return available.ValueKind == JsonValueKind.Null ? null : available.GetBoolean();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or JsonException or InvalidOperationException) { return false; }
    }

    public static async Task<bool> CheckMediaControlAsync(string? path, CancellationToken token = default)
    {
        if (path == null) return false;
        try { await MacProcess.CaptureAsync(MacProcess.StartInfo(path, "test"), token, TimeSpan.FromSeconds(5)).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    public static Task<int> InstallMediaControlAsync(string brewPath, Action<string> output, CancellationToken token = default)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        return InstallAsync(brewPath, output, token);
    }

    internal static async Task<int> InstallAsync(string brewPath, Action<string> output, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info = MacProcess.StartInfo(brewPath, "install", "media-control");
        info.RedirectStandardInput = true;
        info.Environment["HOMEBREW_NO_AUTO_UPDATE"] = "1";
        info.Environment["HOMEBREW_NO_INSTALL_CLEANUP"] = "1";
        info.Environment["NONINTERACTIVE"] = "1";
        using var process = Process.Start(info) ?? throw new IOException("Could not start Homebrew.");
        process.StandardInput.Close();
        using var cancellation = token.Register(() => MacProcess.Kill(process));
        async Task ReadAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) output(line);
        }
        var streams = Task.WhenAll(ReadAsync(process.StandardOutput), ReadAsync(process.StandardError));
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            await streams.ConfigureAwait(false);
            return process.ExitCode;
        }
        finally
        {
            MacProcess.Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await streams.ConfigureAwait(false);
        }
    }

    public static async Task CheckConsoleStartupAsync(bool interactive, CancellationToken token = default)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var status = await ProbeAsync(token).ConfigureAwait(false);
        await HandleConsoleStatusAsync(status, interactive, Console.Error, () => Console.ReadLine(),
            async () =>
            {
                var code = await InstallMediaControlAsync(status.BrewPath!, line => Console.Error.WriteLine(line), token).ConfigureAwait(false);
                return code == 0 && await CheckMediaControlAsync(MacOSMediaControlBackend.ResolveExecutable(), token).ConfigureAwait(false) ? 0 : 1;
            });
    }

    internal static async Task HandleConsoleStatusAsync(MacEnvironmentStatus status, bool interactive,
        TextWriter error, Func<string?> readLine, Func<Task<int>> install)
    {
        if (!status.NeedsInstallation) return;
        error.WriteLine(Localization.Get("MacNoConnection"));
        if (status.BrewPath == null) { error.WriteLine(Localization.Get("MacBrewMissing")); return; }
        if (!interactive) { error.WriteLine(Localization.Get("MacInstallCommandHint")); return; }
        error.Write(Localization.Get("MacInstallQuestion") + " [y/N] ");
        var answer = readLine()?.Trim();
        if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var code = await install().ConfigureAwait(false);
            error.WriteLine(Localization.Get(code == 0 ? "MacInstallFinished" : "MacInstallFailed"));
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { error.WriteLine(Localization.Get("MacInstallFailed") + " " + exception.Message); }
    }
}
