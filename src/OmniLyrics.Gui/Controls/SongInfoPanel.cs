using System;
using Avalonia;
using Avalonia.Controls;

namespace OmniLyrics.Gui.Controls;

/// <summary>Reserves natural space for song details and actions before sizing the artwork.</summary>
public sealed class SongInfoPanel : Panel
{
    public static readonly StyledProperty<bool> SideBySideProperty =
        AvaloniaProperty.Register<SongInfoPanel, bool>(nameof(SideBySide));
    public static readonly StyledProperty<double> ArtworkSizeProperty =
        AvaloniaProperty.Register<SongInfoPanel, double>(nameof(ArtworkSize), 260);
    public bool SideBySide { get => GetValue(SideBySideProperty); set => SetValue(SideBySideProperty, value); }
    public double ArtworkSize { get => GetValue(ArtworkSizeProperty); set => SetValue(ArtworkSizeProperty, value); }
    private double _coverSize;
    private double ActionsGap => Children.Count == 3 && Children[2].IsVisible ? 20 : 0;

    static SongInfoPanel() => AffectsMeasure<SongInfoPanel>(SideBySideProperty, ArtworkSizeProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 3) return default;
        var textWidth = Math.Max(0, availableSize.Width - (SideBySide ? 104 : 0));
        Children[1].Measure(new Size(textWidth, double.PositiveInfinity));
        Children[2].Measure(new Size(textWidth, double.PositiveInfinity));
        var detailsHeight = Children[1].DesiredSize.Height + ActionsGap + Children[2].DesiredSize.Height;
        _coverSize = SideBySide ? 88 : Math.Min(ArtworkSize, availableSize.Width);
        if (!SideBySide && double.IsFinite(availableSize.Height))
            _coverSize = Math.Min(_coverSize, Math.Max(0, availableSize.Height - detailsHeight - 22));
        Children[0].Measure(new Size(_coverSize, _coverSize));
        var height = SideBySide ? Math.Max(_coverSize, detailsHeight) : _coverSize + 22 + detailsHeight;
        return new Size(availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 3) return finalSize;
        Children[0].Arrange(new Rect(SideBySide ? 0 : (finalSize.Width - _coverSize) / 2, 0, _coverSize, _coverSize));
        var x = SideBySide ? 104 : 0;
        var y = SideBySide ? 0 : _coverSize + 22;
        var width = Math.Max(0, finalSize.Width - x);
        Children[1].Arrange(new Rect(x, y, width, Children[1].DesiredSize.Height));
        Children[2].Arrange(new Rect(x, y + Children[1].DesiredSize.Height + ActionsGap, width, Children[2].DesiredSize.Height));
        return finalSize;
    }
}
