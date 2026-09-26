using System.Diagnostics;

namespace OmniLyrics.Backends.Mac;

/// <summary>Owns child processes, including cancellation while a pipe is idle.</summary>
internal static class MacProcess
{
    internal static ProcessStartInfo StartInfo(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    internal static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    internal static async Task StreamAsync(ProcessStartInfo info, Action<string> receive, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(info) ?? throw new IOException("Could not start " + info.FileName);
        using var cancellation = token.Register(() => Kill(process));
        var errors = process.StandardError.ReadToEndAsync(token);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                if (!token.IsCancellationRequested) receive(line);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var detail = await errors.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            throw new IOException($"{Path.GetFileName(info.FileName)} exited ({process.ExitCode}): {detail.Trim()}");
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await errors.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    internal static async Task RunAsync(ProcessStartInfo info, CancellationToken token) =>
        _ = await CaptureAsync(info, token, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

    internal static async Task<string> CaptureAsync(ProcessStartInfo info, CancellationToken token, TimeSpan duration)
    {
        token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(duration);
        using var process = Process.Start(info) ?? throw new IOException("Could not start " + info.FileName);
        using var cancellation = timeout.Token.Register(() => Kill(process));
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var result = await output.ConfigureAwait(false);
            var detail = await errors.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException($"{Path.GetFileName(info.FileName)} failed ({process.ExitCode}): {detail.Trim()}");
            return result;
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(output, errors).ConfigureAwait(false);
        }
    }
}
