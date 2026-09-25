using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using OmniLyrics.Core.Lyrics;
using OmniLyrics.Core.Lyrics.Models;

namespace OmniLyrics.Gui.Controls;

/// <summary>Shaped text with a time-based clip for each real timed syllable.</summary>
public sealed class KaraokeLine : Control
{
    public static readonly StyledProperty<LyricsLine?> LineProperty =
        AvaloniaProperty.Register<KaraokeLine, LyricsLine?>(nameof(Line));
    public static readonly StyledProperty<TimeSpan> PositionProperty =
        AvaloniaProperty.Register<KaraokeLine, TimeSpan>(nameof(Position));
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<KaraokeLine, double>(nameof(FontSize), 28);
    public static readonly StyledProperty<bool> WrapProperty = AvaloniaProperty.Register<KaraokeLine, bool>(nameof(Wrap));
    public static readonly StyledProperty<bool> AlignLeftProperty = AvaloniaProperty.Register<KaraokeLine, bool>(nameof(AlignLeft));
    public static readonly StyledProperty<bool> ApproximateProperty = AvaloniaProperty.Register<KaraokeLine, bool>(nameof(Approximate));
    public static readonly StyledProperty<bool> DrawShadowProperty = AvaloniaProperty.Register<KaraokeLine, bool>(nameof(DrawShadow), true);
    public static readonly StyledProperty<TimeSpan> LineEndProperty = AvaloniaProperty.Register<KaraokeLine, TimeSpan>(nameof(LineEnd));
    public static readonly StyledProperty<IBrush> HighlightBrushProperty =
        AvaloniaProperty.Register<KaraokeLine, IBrush>(nameof(HighlightBrush), new SolidColorBrush(Color.Parse("#FFF3C879")));
    public static readonly StyledProperty<IBrush> BaseBrushProperty =
        AvaloniaProperty.Register<KaraokeLine, IBrush>(nameof(BaseBrush), new SolidColorBrush(Color.Parse("#A8FFFFFF")));
    private double _layoutWidth = double.PositiveInfinity;

    private TextLayout? _baseText;
    private TextLayout? _highlight;
    private TextLayout? _shadow;
    private IReadOnlyList<TimedTextRange> _ranges = Array.Empty<TimedTextRange>();

    static KaraokeLine()
    {
        AffectsRender<KaraokeLine>(LineProperty, PositionProperty, FontSizeProperty, WrapProperty, AlignLeftProperty,
            ApproximateProperty, LineEndProperty, HighlightBrushProperty, BaseBrushProperty, DrawShadowProperty);
        AffectsMeasure<KaraokeLine>(LineProperty, FontSizeProperty, WrapProperty);
    }

    public LyricsLine? Line { get => GetValue(LineProperty); set => SetValue(LineProperty, value); }
    public TimeSpan Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public bool Wrap { get => GetValue(WrapProperty); set => SetValue(WrapProperty, value); }
    public bool AlignLeft { get => GetValue(AlignLeftProperty); set => SetValue(AlignLeftProperty, value); }
    public bool Approximate { get => GetValue(ApproximateProperty); set => SetValue(ApproximateProperty, value); }
    public bool DrawShadow { get => GetValue(DrawShadowProperty); set => SetValue(DrawShadowProperty, value); }
    public TimeSpan LineEnd { get => GetValue(LineEndProperty); set => SetValue(LineEndProperty, value); }
    public IBrush HighlightBrush { get => GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }
    public IBrush BaseBrush { get => GetValue(BaseBrushProperty); set => SetValue(BaseBrushProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LineProperty || change.Property == FontSizeProperty || change.Property == WrapProperty || change.Property == AlignLeftProperty
            || change.Property == HighlightBrushProperty || change.Property == BaseBrushProperty)
        {
            ClearLayout();
            _ranges = KaraokeTimeline.GetRanges(Line);
        }
    }

    private void ClearLayout()
    {
        _baseText?.Dispose(); _highlight?.Dispose(); _shadow?.Dispose();
        _baseText = _highlight = _shadow = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ClearLayout();
    }

    private void EnsureLayout(double availableWidth)
    {
        var targetWidth = Wrap ? Math.Max(1, availableWidth) : double.PositiveInfinity;
        if (_layoutWidth != targetWidth) { ClearLayout(); _layoutWidth = targetWidth; }
        if (_baseText != null) return;
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
        var text = Line?.Text ?? "";
        TextLayout Layout(IBrush brush) => new(text, typeface, FontSize, brush,
            textAlignment: Wrap && !AlignLeft ? TextAlignment.Center : TextAlignment.Left,
            textWrapping: Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, maxWidth: targetWidth);
        _baseText = Layout(BaseBrush);
        _highlight = Layout(HighlightBrush);
        _shadow = Layout(new SolidColorBrush(Color.Parse("#D0000000")));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayout(availableSize.Width);
        return new Size(Math.Min(availableSize.Width, _baseText!.WidthIncludingTrailingWhitespace), _baseText.Height + 4);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (string.IsNullOrEmpty(Line?.Text)) return;
        EnsureLayout(Bounds.Width);
        var width = Math.Max(1, _baseText!.WidthIncludingTrailingWhitespace);
        var scale = Wrap ? Math.Min(1, Bounds.Height / Math.Max(1, _baseText.Height)) : Math.Min(1, Bounds.Width / width);
        var x = AlignLeft ? 0 : (Bounds.Width - (Wrap ? Bounds.Width : width) * scale) / 2;
        var y = (Bounds.Height - _baseText.Height * scale) / 2;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y));
        if (DrawShadow) _shadow!.Draw(context, new Point(1.5, 1.5));
        if (_ranges.Count == 0)
        {
            if (Approximate && LineEnd > Line.Timestamp)
            {
                _baseText.Draw(context, default);
                DrawProgress(context, 0, Line.Text.Length, KaraokeTimeline.ApproximateProgress(Line, LineEnd, Position));
            }
            else _highlight!.Draw(context, default);
            return;
        }
        _baseText.Draw(context, default);
        foreach (var range in _ranges)
        {
            var progress = range.ProgressAt(Position);
            if (progress <= 0) continue;
            DrawProgress(context, range.Start, range.Length, progress);
        }
    }

    private void DrawProgress(DrawingContext context, int start, int length, double progress)
    {
        var rectangles = _baseText!.HitTestTextRange(start, length);
        double total = 0;
        foreach (var rect in rectangles) total += rect.Width;
        var remaining = total * progress;
        foreach (var rect in rectangles)
        {
            var width = Math.Clamp(remaining, 0, rect.Width);
            if (width > 0)
                using (context.PushClip(new Rect(rect.X, rect.Y, width, rect.Height))) _highlight!.Draw(context, default);
            remaining -= rect.Width;
        }
    }
}
