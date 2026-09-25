namespace OmniLyrics.Core.Configuration;

/// <summary>A color preset never changes layout, typography, visibility or window behavior.</summary>
public sealed record ThemePalette(string ThemeMode, string AccentColor, string TextColor,
    string HighlightColor, string BackgroundColor)
{
    public static IReadOnlyDictionary<string, ThemePalette> BuiltIn { get; } =
        new Dictionary<string, ThemePalette>
        {
            ["DarkRed"] = new("dark", "#FA586A", "#BCC0CB", "#FF6376", "#19191F"),
            ["DarkBlue"] = new("dark", "#4C9AFF", "#BCC7DA", "#70AEFF", "#171D29"),
            ["LightBlue"] = new("light", "#0067C8", "#626878", "#0067C8", "#F5F7FB"),
            ["LightRed"] = new("light", "#C92649", "#73646A", "#C92649", "#FCF5F7")
        };

    public static ThemePalette FromAppearance(AppearanceSettings appearance) => new(appearance.ThemeMode,
        appearance.AccentColor, appearance.TextColor, appearance.HighlightColor, appearance.BackgroundColor);

    public AppearanceSettings Apply(AppearanceSettings appearance) => appearance with
    {
        ThemeMode = ThemeMode, AccentColor = AccentColor, TextColor = TextColor,
        HighlightColor = HighlightColor, BackgroundColor = BackgroundColor
    };

    public void Validate()
    {
        if (ThemeMode is not ("dark" or "light")) throw new ArgumentException("Theme must be dark or light.");
        foreach (var color in new[] { AccentColor, TextColor, HighlightColor, BackgroundColor })
            if (color == null || !System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
                throw new ArgumentException("Colors must use #RRGGBB format.");
    }
}

public sealed record NamedThemePreset(string Name, ThemePalette Palette)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 64 || Name != Name.Trim() || Name.Any(char.IsControl))
            throw new ArgumentException("Use a theme name between 1 and 64 characters.");
        if (Palette == null) throw new ArgumentException("A theme palette is required.");
        Palette.Validate();
    }
}
