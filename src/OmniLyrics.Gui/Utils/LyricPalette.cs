using System;
using Avalonia.Media;
using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Gui.Utils;

/// <summary>Contrast for the lyric surface, independent of the settings window's theme.</summary>
internal sealed record LyricPalette(bool Dark, bool Translucent, Color Canvas, Color Text, Color Highlight,
    Color Primary, Color Secondary, Color Muted, Color Accent, Color Outline, Color ControlSurface)
{
    internal static bool NativeMacBlur(AppearanceSettings settings) => OperatingSystem.IsMacOS() && settings.UseBlur;
    internal static double BackgroundOpacity(AppearanceSettings settings) => NativeMacBlur(settings) ? 0 : settings.BackgroundOpacity;

    internal static LyricPalette Create(AppearanceSettings settings)
    {
        var canvas = NativeMacBlur(settings)
            ? Color.Parse(settings.ThemeMode == "dark" ? "#202026" : "#F3F3F7")
            : Color.Parse(settings.BackgroundColor);
        var dark = Luminance(canvas) < .179;
        var translucent = BackgroundOpacity(settings) * canvas.A / 255 < .99;
        var surface = Color.Parse(dark ? "#161A23" : "#F6F6F8");
        Color Ink(string color) => Readable(Color.Parse(color), translucent ? surface : canvas, translucent ? 5.5 : 4.5);
        Color LyricInk(string color) => Readable(Color.Parse(color), translucent ? dark ? Colors.Black : Colors.White : canvas);
        return new(dark, translucent, canvas, LyricInk(settings.TextColor), LyricInk(settings.HighlightColor),
            Ink(dark ? "#F5F4F7" : "#25252C"), Ink(dark ? "#B6BAC6" : "#5F6372"),
            Ink(dark ? "#979EAF" : "#686C7A"), Ink(settings.AccentColor), dark ? Colors.Black : Colors.White,
            Color.FromArgb(translucent ? (byte)240 : (byte)0, surface.R, surface.G, surface.B));
    }

    internal static Color Readable(Color ink, Color background, double minimumContrast = 4.5)
    {
        ink = Color.FromRgb(ink.R, ink.G, ink.B);
        var target = Luminance(background) < .179 ? Colors.White : Colors.Black;
        var candidate = ink;
        for (int step = 1; Contrast(candidate, background) < minimumContrast && step <= 100; step++)
            candidate = Color.FromRgb((byte)(ink.R + (target.R - ink.R) * step / 100d),
                (byte)(ink.G + (target.G - ink.G) * step / 100d), (byte)(ink.B + (target.B - ink.B) * step / 100d));
        return candidate;
    }

    internal static double Contrast(Color a, Color b) => (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
    private static double Luminance(Color color)
    {
        static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
}
