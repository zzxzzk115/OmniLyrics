using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OmniLyrics.Gui.Utils;

/// <summary>XWayland's Xft.dpi can differ from the actual output scale.</summary>
internal sealed record HyprlandScaleSnapshot(Dictionary<string, double> Windows, bool ZeroScaling)
{
    public static HyprlandScaleSnapshot Parse(string monitors, string clients, string option, int processId)
    {
        using var outputs = JsonDocument.Parse(monitors);
        using var windows = JsonDocument.Parse(clients);
        using var setting = JsonDocument.Parse(option);
        var zero = setting.RootElement.TryGetProperty("bool", out var flag)
            ? flag.GetBoolean() : setting.RootElement.GetProperty("int").GetInt32() != 0;
        var scales = outputs.RootElement.EnumerateArray().ToDictionary(
            m => m.GetProperty("id").GetInt32(), m => m.GetProperty("scale").GetDouble());
        var result = new Dictionary<string, double>();
        foreach (var window in windows.RootElement.EnumerateArray())
        {
            if (window.GetProperty("pid").GetInt32() != processId || !window.GetProperty("xwayland").GetBoolean()) continue;
            if (scales.TryGetValue(window.GetProperty("monitor").GetInt32(), out var scale)
                && double.IsFinite(scale) && scale is >= .5 and <= 4)
                result[window.GetProperty("title").GetString() ?? ""] = scale;
        }
        return new(result, zero);
    }

    public double? Factor(string title, double nativeScale, double? requestedScale)
        => Windows.TryGetValue(title, out var scale)
            ? (requestedScale ?? scale) / (nativeScale * (ZeroScaling ? 1 : scale)) : null;
}
