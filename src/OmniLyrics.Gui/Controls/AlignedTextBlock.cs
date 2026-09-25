using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace OmniLyrics.Gui.Controls;

/// <summary>Keeps alignment after color or text changes that leave the measured size unchanged.</summary>
public sealed class AlignedTextBlock : TextBlock
{
    protected override TextLayout CreateTextLayout(string? text)
    {
        if (Inlines is { Count: > 0 }) return base.CreateTextLayout(text);
        // TextBlock's measurement layout forces left alignment. A foreground change can
        // retain that layout when size is unchanged, so retain the requested alignment here too.
        return new TextLayout(text ?? "", new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize, Foreground, textAlignment: TextAlignment, textWrapping: TextWrapping,
            textTrimming: TextTrimming, textDecorations: TextDecorations, flowDirection: FlowDirection,
            maxWidth: double.IsNaN(_constraint.Width) ? 0 : _constraint.Width,
            maxHeight: double.IsNaN(_constraint.Height) ? 0 : _constraint.Height,
            lineHeight: LineHeight, letterSpacing: LetterSpacing, maxLines: MaxLines);
    }
}
