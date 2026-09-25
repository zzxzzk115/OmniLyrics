using System;
using Avalonia;
using Avalonia.Controls;

namespace OmniLyrics.Gui.Controls;

/// <summary>Centers the active original/translation group and flows context around it.
/// Natural child heights prevent long context lines from overlapping the active group.</summary>
public sealed class CenteredLyricsPanel : Panel
{
    private const double Gap = 32;
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 2 * Gap;
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
        }
        return new Size(Math.Min(width, availableSize.Width), double.IsFinite(availableSize.Height) ? availableSize.Height : height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 3) return finalSize;
        var activeHeight = Children[1].DesiredSize.Height;
        var activeTop = (finalSize.Height - activeHeight) / 2;
        Children[0].Arrange(new Rect(0, activeTop - Gap - Children[0].DesiredSize.Height, finalSize.Width, Children[0].DesiredSize.Height));
        Children[1].Arrange(new Rect(0, activeTop, finalSize.Width, activeHeight));
        Children[2].Arrange(new Rect(0, activeTop + activeHeight + Gap, finalSize.Width, Children[2].DesiredSize.Height));
        return finalSize;
    }
}
