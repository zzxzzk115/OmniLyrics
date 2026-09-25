using System.Text.Encodings.Web;
using System.Text.Json;

namespace OmniLyrics.Core.Cli;

/// <summary>Streams changed lyric snapshots to desktop widgets without terminal control codes.</summary>
public sealed class JsonLyricsCli(IPlayerBackend backend) : BaseLyricsCli(backend)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private long _nextRenderAt;
    private string? _lastOutput;

    protected override void RenderLyricsFrame()
    {
        if (Environment.TickCount64 < _nextRenderAt)
            return;
        _nextRenderAt = Environment.TickCount64 + 100;

        var state = Backend.GetCurrentState();
        var snapshot = CaptureLyrics(state);
        var lines = snapshot.Lines;
        var index = state == null || lines == null ? -1
            : lines.FindLastIndex(line => line.Timestamp <= state.Position);

        var output = JsonSerializer.Serialize(new
        {
            available = state != null,
            title = state?.Title ?? "",
            artist = state == null ? "" : string.Join(", ", state.Artists),
            sourceApp = state?.SourceApp ?? "",
            playing = state?.Playing ?? false,
            loading = snapshot.Loading,
            hasLyrics = lines is { Count: > 0 },
            currentLine = index >= 0 ? lines![index].Text : "",
            nextLine = lines != null && index + 1 < lines.Count ? lines[index + 1].Text : "",
            currentTranslation = index >= 0 ? lines![index].Translation ?? "" : "",
            nextTranslation = lines != null && index + 1 < lines.Count ? lines[index + 1].Translation ?? "" : ""
        }, JsonOptions);

        if (output == _lastOutput)
            return;
        _lastOutput = output;
        Console.WriteLine(output);
        Console.Out.Flush();
    }

    protected override Task RedrawScreenAsync(string a, string b, string c, List<string>? d)
        => Task.CompletedTask;
}
