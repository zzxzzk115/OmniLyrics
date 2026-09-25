using System;
using Avalonia;
using Avalonia.Controls;

namespace OmniLyrics.Gui.Controls;

/// <summary>Updates the layout before child measurement, never halfway through arrangement.</summary>
public sealed class ResponsiveBorder : Border
{
    public Action<Size>? Measuring { get; set; }
    private Size _lastSize;
    protected override Size MeasureOverride(Size availableSize)
    {
        if (availableSize != _lastSize && double.IsFinite(availableSize.Width) && double.IsFinite(availableSize.Height))
        {
            _lastSize = availableSize;
            Measuring?.Invoke(availableSize);
        }
        return base.MeasureOverride(availableSize);
    }
}
