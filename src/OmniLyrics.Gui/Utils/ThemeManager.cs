using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Gui.Models;

namespace OmniLyrics.Gui.Utils;

public static class ThemeManager
{
    public static void Apply(AppearanceSettings settings)
    {
        if (Application.Current is not { } app) return;
        var dark = settings.ThemeMode == "dark";
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        void Brush(string key, string color) => app.Resources[key] = new SolidColorBrush(Color.Parse(color));
        Brush("SettingsBackground", dark ? "#18181E" : "#F5F5F7");
        Brush("SidebarBackground", dark ? "#202026" : "#E9E9EE");
        Brush("SurfaceBackground", dark ? "#25252D" : "#FFFFFF");
        Brush("FieldBackground", dark ? "#31313B" : "#E9E9EF");
        Brush("ButtonBackground", dark ? "#36363F" : "#E5E5EC");
        Brush("HoverBackground", dark ? "#3C3A45" : "#DADAE3");
        Brush("SubtleBorder", dark ? "#35353F" : "#DEDEE6");
        Brush("SeparatorBrush", dark ? "#3A3943" : "#E2E1E7");
        Brush("PrimaryText", dark ? "#F5F4F7" : "#25252C");
        Brush("SecondaryText", dark ? "#A9A7B3" : "#626170");
        Brush("ScrollbarBrush", dark ? "#777380" : "#9B98A4");
        Brush("WarningText", dark ? "#EDBB75" : "#88510A");
        Brush("ErrorText", dark ? "#FFA3AC" : "#AC1A37");
        var accent = Color.Parse(settings.AccentColor);
        Brush("AccentBrush", settings.AccentColor);
        // Filled controls keep a bright label; derive a darker tone when the
        // chosen accent is too light to provide 4.5:1 contrast with white.
        var accentFill = ReadableFill(accent);
        app.Resources["AccentFillBrush"] = new SolidColorBrush(accentFill);
        Brush("AccentForeground", "#FFFFFF");
        app.Resources["AccentHoverBrush"] = new SolidColorBrush(Mix(accentFill, Colors.Black, .12));
        app.Resources["SystemAccentColor"] = accent;
        foreach (var key in new[] { "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
                     "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3" })
            app.Resources[key] = accent;
        foreach (var key in new[] { "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
                     "SliderTrackValueFill", "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed",
                     "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedStroke", "TextControlBorderBrushFocused" })
            Brush(key, settings.AccentColor);

        // Custom canvas colors can differ from the chrome theme. Keep the song and its controls readable.
        var darkCanvas = Luminance(Color.Parse(settings.BackgroundColor)) < .35;
        Brush("LyricPrimary", darkCanvas ? "#F5F4F7" : "#25252C");
        Brush("LyricSecondary", darkCanvas ? "#B6BAC6" : "#5F6372");
        Brush("LyricMuted", darkCanvas ? "#979EAF" : "#686C7A");
        Brush("LyricButtonBackground", darkCanvas ? "#20FFFFFF" : "#15000000");
        Brush("LyricHoverBackground", darkCanvas ? "#35FFFFFF" : "#23000000");
        Brush("CoverPlaceholder", darkCanvas ? "#283142" : "#E1E5EE");
        Brush("ProgressBackground", darkCanvas ? "#3B4253" : "#D6D9E2");
    }

    private static Color Mix(Color color, Color target, double amount) => Color.FromRgb(
        (byte)(color.R + (target.R - color.R) * amount),
        (byte)(color.G + (target.G - color.G) * amount),
        (byte)(color.B + (target.B - color.B) * amount));

    private static Color ReadableFill(Color accent)
    {
        var fill = Color.FromRgb(accent.R, accent.G, accent.B);
        for (var step = 1; 1.05 / (Luminance(fill) + .05) < 4.5 && step <= 100; step++)
            fill = Mix(accent, Colors.Black, step / 100d);
        return fill;
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
}
