using OmniLyrics.Core;
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OmniLyrics.Backends.CiderV3;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Gui.Models;
using Avalonia.Media;
using System.Linq;
using Avalonia.LogicalTree;
using OmniLyrics.Gui.Utils;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniLyrics.Gui;

public partial class SettingsWindow : Window
{
    public bool KeepAlive { get; set; }
    private bool _reloading;
    private bool _savingScale;
    private double _solidBackgroundOpacity = 60;
    private AppearanceSettings? _lastWindowState;
    private readonly ConfigurationWatcher _watcher;
    private readonly WindowScale _windowScale;
    private CiderConnectionSettings? _loadedCider;
    private bool _settingPalette;
    private sealed record ThemeOption(string Label, ThemePalette? Palette = null, string? SavedName = null)
    {
        public override string ToString() => Label;
    }

    public SettingsWindow()
    {
        InitializeComponent();
        InitializeMacEnvironment();
        InitializeFavorites();
        BlurMode.PropertyChanged += (_, change) =>
        {
            if (change.Property == Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty && !_reloading)
                UpdateBlurOptions();
        };
        _windowScale = new WindowScale(this, size => ClientSize = size, scrollWhenConstrained: true);
        if (OperatingSystem.IsLinux())
            X11Properties.SetNetWmWindowType(this, Avalonia.Controls.Platform.X11NetWmWindowType.Dialog);
        Deactivated += (_, _) => ReleaseSearchFocus();
        Localization.Changed += RefreshLanguage;
        Closed += (_, _) => Localization.Changed -= RefreshLanguage;
        AppearancePreferences.Changed += RefreshWindowState;
        Closed += (_, _) => AppearancePreferences.Changed -= RefreshWindowState;
        VersionText.Text = Localization.Format("Version", ApplicationInfo.Version);
        _watcher = new();
        _watcher.Changed += () =>
        {
            AppearancePreferences.Refresh();
            Localization.Reload();
            ReloadSettings(preserveToken: true);
            AppearanceStatus.Text = Localization.Get("ConfigurationReloaded");
        };
        _watcher.Invalid += () => AppearanceStatus.Text = Localization.Get("ConfigurationInvalid");
        Closed += (_, _) => _watcher.Dispose();
        Closing += (_, e) =>
        {
            if (KeepAlive && e.CloseReason is WindowCloseReason.WindowClosing or WindowCloseReason.Undefined)
            {
                TokenInput.Text = "";
                Hide();
                e.Cancel = true;
            }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible) ReloadSettings();
        };
        ReloadSettings();
    }

    internal void RecoverWindowPlacement()
    {
        WindowState = WindowState.Normal;
        _windowScale.SetSize(new Avalonia.Size(980, 760));
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return;
        var work = screen.WorkingArea;
        var size = Avalonia.PixelSize.FromSize(FrameSize ?? ClientSize, RenderScaling);
        Position = new Avalonia.PixelPoint(work.X + Math.Max(0, (work.Width - size.Width) / 2),
            work.Y + Math.Max(0, (work.Height - size.Height) / 2));
    }

    internal void OpenAbout() { SettingsSearch.Text = ""; SettingsTabs.SelectedItem = AboutTab; }

    private void ReloadSettings(bool preserveToken = false)
    {
        _reloading = true;
        if (!preserveToken) TokenInput.Text = "";
        StatusText.Text = "";
        ApplyButton.IsEnabled = true;
        TestButton.IsEnabled = true;
        OverrideNotice.IsVisible = UserConfiguration.HasEnvironmentOverride;
        try
        {
            UserConfiguration.ValidateConfigurationText(UserConfiguration.ReadConfigurationText());
            LanguageMode.SelectedIndex = UserConfiguration.LoadLanguage() switch { "zh-CN" => 1, "en" => 2, _ => 0 };
            var appearance = UserConfiguration.LoadAppearance();
            LoadScale(appearance.UiScale);
            _lastWindowState = appearance;
            PresetMode.SelectedIndex = appearance.Preset switch { "compact" => 1, "focus" => 2, "portrait" => 3, "fullscreen" => 4, _ => 0 };
            ShowLogoMode.IsChecked = appearance.ShowLogo;
            ShowPlayerMode.IsChecked = appearance.ShowPlayerInfo;
            LockedMode.IsChecked = appearance.Locked;
            ApproximateMode.IsChecked = appearance.ApproximateHighlight;
            TranslationMode.IsChecked = appearance.ShowTranslation;
            LyricFontSize.Value = (decimal)appearance.FontSize;
            TranslationSize.Value = (decimal)appearance.TranslationFontSize;
            TextColorPicker.Color = Color.Parse(appearance.TextColor);
            HighlightColorPicker.Color = Color.Parse(appearance.HighlightColor);
            BackgroundColorPicker.Color = Color.Parse(appearance.BackgroundColor);
            ThemeMode.SelectedIndex = appearance.ThemeMode == "light" ? 1 : 0;
            AccentColorPicker.Color = Color.Parse(appearance.AccentColor);
            ReloadThemePresets();
            OpacitySlider.Value = appearance.BackgroundOpacity * 100;
            BlurMode.IsChecked = appearance.UseBlur;
            if (!appearance.UseBlur || appearance.BackgroundOpacity > 0) _solidBackgroundOpacity = appearance.BackgroundOpacity * 100;
            UpdateBlurOptions();
            AppearanceStatus.Text = "";
            var lyrics = UserConfiguration.LoadLyrics();
            PrefetchEnabled.IsChecked = lyrics.Prefetch;
            PrefetchCount.Value = lyrics.PrefetchCount;
            SearchStrategyMode.SelectedIndex = lyrics.SearchStrategy == "quick" ? 1 : 0;
            MatchMode.SelectedIndex = lyrics.MatchMode == "strict" ? 1 : 0;
            PlayerLyricsEnabled.IsChecked = lyrics.PlayerSource;
            QQLyricsEnabled.IsChecked = lyrics.QQMusicSource;
            NeteaseLyricsEnabled.IsChecked = lyrics.NeteaseSource;
            PreferredLyricSourceMode.SelectedIndex = lyrics.PreferredSource == "netease" ? 1 : 0;
            LyricsStatus.Text = "";
            var settings = UserConfiguration.LoadCider();
            _loadedCider = settings;
            TokenMode.IsChecked = settings.Authentication == "token";
            IntegrationMode.SelectedIndex = settings.Integration switch { "webapi" => 1, "mpris" => 2, _ => 0 };
            NoTokenMode.IsChecked = TokenMode.IsChecked != true;
            UpdateTokenHint();
            ReloadFavorites(preserveToken);
        }
        catch
        {
            LyricsStatus.Text = Localization.Text("Cannot read preferences. Check the configuration file.");
            StatusText.Text = Localization.Text("Cannot read configuration. Check the file and permissions before saving.");
            ApplyButton.IsEnabled = false;
            TestButton.IsEnabled = false;
        }
        finally { _reloading = false; }
    }

    private void UpdateBlurOptions()
    {
        var nativeBlur = OperatingSystem.IsMacOS() && BlurMode.IsChecked == true;
        MacBlurOpacityHint.IsVisible = nativeBlur;
        if (nativeBlur)
        {
            if (OpacitySlider.IsEnabled && !_reloading) _solidBackgroundOpacity = OpacitySlider.Value;
            OpacitySlider.Value = 0;
            OpacitySlider.IsEnabled = false;
        }
        else
        {
            if (!OpacitySlider.IsEnabled) OpacitySlider.Value = _solidBackgroundOpacity;
            OpacitySlider.IsEnabled = true;
        }
    }

    private void UpdateTokenHint()
    {
        TokenInput.Watermark = string.IsNullOrEmpty(UserConfiguration.ReadSavedToken())
            ? Localization.Text("Enter API token") : Localization.Text("Token saved · leave blank to keep it");
    }

    private string? ProposedToken()
    {
        if (TokenMode.IsChecked != true) return "";
        var typed = TokenInput.Text?.Trim();
        var token = string.IsNullOrEmpty(typed) ? UserConfiguration.ReadSavedToken() : typed;
        if (string.IsNullOrEmpty(token) || token.Contains('\r') || token.Contains('\n'))
        {
            StatusText.Text = Localization.Text("Enter a valid single-line token, or choose No token.");
            return null;
        }
        return token;
    }


    private async void TestButton_Click(object? sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        try
        {
            var token = ProposedToken();
            if (token is null) return;
            StatusText.Text = Localization.Text("Connecting to Cider…");
            using var api = new CiderV3Api(appToken: token);
            StatusText.Text = await api.TryGetActiveAsync()
                ? Localization.Text("Connected with these settings. No library changes were made.")
                : Localization.Text("Cannot connect. Check Cider, Require API Tokens and the token's playback permission.");
        }
        catch
        {
            StatusText.Text = Localization.Text("Could not read the token. Check the configuration and permissions.");
        }
        finally { TestButton.IsEnabled = true; }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();


    private void LanguageMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_reloading || LanguageMode.SelectedIndex < 0) return;
        try
        {
            UserConfiguration.SaveLanguage(LanguageMode.SelectedIndex switch { 1 => "zh-CN", 2 => "en", _ => "auto" });
            Localization.Reload();
            _watcher.AcceptCurrent();
            VersionText.Text = Localization.Format("Version", ApplicationInfo.Version);
            UpdateTokenHint();
            LyricsStatus.Text = Localization.Get("LanguageSaved");
        }
        catch { LyricsStatus.Text = Localization.Get("PreferencesSaveError"); }
    }


    private static readonly double[] ScalePresets = [1, 1.25, 1.5, 1.75, 2];

    private void LoadScale(double? scale)
    {
        var reloading = _reloading;
        _reloading = true;
        try
        {
            var index = scale is { } value ? Array.IndexOf(ScalePresets, value) : -1;
            UiScaleMode.SelectedIndex = scale == null ? 0 : index < 0 ? 6 : index + 1;
            CustomUiScale.Value = (decimal)((scale ?? 1) * 100);
            CustomScaleRow.IsVisible = UiScaleMode.SelectedIndex == 6;
        }
        finally { _reloading = reloading; }
    }

    private void UiScaleMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_reloading || CustomScaleRow == null || CustomUiScale == null || UiScaleMode.SelectedIndex < 0) return;
        CustomScaleRow.IsVisible = UiScaleMode.SelectedIndex == 6;
        if (UiScaleMode.SelectedIndex == 6) return;
        SaveScale(UiScaleMode.SelectedIndex == 0 ? null : ScalePresets[UiScaleMode.SelectedIndex - 1]);
    }

    private void CustomUiScale_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!_reloading && UiScaleMode?.SelectedIndex == 6 && e.NewValue is >= 75 and <= 300)
            SaveScale((double)e.NewValue.Value / 100);
    }

    private void SaveScale(double? scale)
    {
        try
        {
            _savingScale = true;
            AppearancePreferences.Save(AppearancePreferences.Current with { UiScale = scale });
            _watcher.AcceptCurrent();
        }
        catch { AppearanceStatus.Text = Localization.Get("PreferencesSaveError"); }
        finally { _savingScale = false; }
    }

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var cider = new CiderConnectionSettings(TokenMode.IsChecked == true ? "token" : "none",
                IntegrationMode.SelectedIndex switch { 1 => "webapi", 2 => "mpris", _ => "auto" });
            var changedCider = cider != _loadedCider || !string.IsNullOrWhiteSpace(TokenInput.Text);
            if (changedCider && ProposedToken() is null) { SettingsTabs.SelectedItem = PlayerConnectionsTab; return; }
            var appearance = new AppearanceSettings(PresetMode.SelectedIndex switch { 1 => "compact", 2 => "focus", 3 => "portrait", 4 => "fullscreen", _ => "classic" },
                LockedMode.IsChecked == true, ShowLogoMode.IsChecked == true, ShowPlayerMode.IsChecked == true,
                ApproximateMode.IsChecked == true, TranslationMode.IsChecked == true,
                (double)(LyricFontSize.Value ?? 32), (double)(TranslationSize.Value ?? 18),
                Rgb(TextColorPicker.Color), Rgb(HighlightColorPicker.Color), Rgb(BackgroundColorPicker.Color),
                OperatingSystem.IsMacOS() && BlurMode.IsChecked == true ? 0 : OpacitySlider.Value / 100, BlurMode.IsChecked == true,
                ThemeMode.SelectedIndex == 1 ? "light" : "dark", Rgb(AccentColorPicker.Color), AppearancePreferences.Current.UiScale);
            if (PlayerLyricsEnabled.IsChecked != true && QQLyricsEnabled.IsChecked != true && NeteaseLyricsEnabled.IsChecked != true)
            {
                SettingsTabs.SelectedItem = LyricsTab;
                AppearanceStatus.Text = Localization.Get("LyricSourceRequired");
                return;
            }
            var lyrics = new LyricsSettings(PrefetchEnabled.IsChecked == true, (int)(PrefetchCount.Value ?? 5),
                SearchStrategyMode.SelectedIndex == 1 ? "quick" : "fallback", MatchMode.SelectedIndex == 1 ? "strict" : "balanced",
                PlayerLyricsEnabled.IsChecked == true, QQLyricsEnabled.IsChecked == true, NeteaseLyricsEnabled.IsChecked == true,
                PreferredLyricSourceMode.SelectedIndex == 1 ? "netease" : "qq");
            UserConfiguration.SavePreferences(appearance, lyrics,
                changedCider ? cider : null, changedCider && cider.Authentication == "token" ? TokenInput.Text : null);
            if (SpotifyClientIdInput.Text != _loadedSpotifyClientId) UserConfiguration.SaveSpotifyClientId(SpotifyClientIdInput.Text ?? "");
            _watcher.AcceptCurrent();
            AppearancePreferences.Refresh();
            ReloadSettings();
            AppearanceStatus.Text = Localization.Get("SettingsApplied");
        }
        catch { AppearanceStatus.Text = Localization.Get("PreferencesSaveError"); }
    }

    private void SettingsTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SettingsTabs == null || PageTitle == null || SettingsTabs.SelectedItem is not TabItem tab) return;
        var key = (tab.Tag?.ToString() ?? "General").Split(' ')[0];
        PageTitle.Text = Localization.Get(key);
        PageSubtitle.Text = Localization.Get(key + "Subtitle");
        if (tab == MacEnvironmentTab) _ = RefreshMacEnvironmentAsync();
    }

    private async void EditConfiguration_Click(object? sender, RoutedEventArgs e) => await new ConfigurationEditorWindow().ShowDialog(this);

    private static string Rgb(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private ThemePalette DraftPalette() => new(ThemeMode.SelectedIndex == 1 ? "light" : "dark", Rgb(AccentColorPicker.Color),
        Rgb(TextColorPicker.Color), Rgb(HighlightColorPicker.Color), Rgb(BackgroundColorPicker.Color));

    private void ReloadThemePresets(string? selectedName = null)
    {
        _settingPalette = true;
        try
        {
            var options = new[] { new ThemeOption(Localization.Get("CustomTheme")) }
                .Concat(ThemePalette.BuiltIn.Select(p => new ThemeOption(Localization.Get(p.Key), p.Value)))
                .Concat(UserConfiguration.LoadThemePresets().Select(p => new ThemeOption(p.Name, p.Palette, p.Name))).ToList();
            ThemePresetMode.ItemsSource = options;
            var draft = DraftPalette();
            ThemePresetMode.SelectedItem = options.FirstOrDefault(p => selectedName != null && p.SavedName == selectedName)
                ?? options.FirstOrDefault(p => p.Palette == draft) ?? options[0];
            UpdateThemeActions();
        }
        finally { _settingPalette = false; }
    }

    private void UpdateThemeActions()
    {
        var savedName = (ThemePresetMode.SelectedItem as ThemeOption)?.SavedName;
        DeleteThemeButton.IsVisible = savedName != null;
        ThemePresetName.Text = savedName ?? "";
    }

    private void ThemePresetMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_reloading || _settingPalette || ThemePresetMode.SelectedItem is not ThemeOption option) return;
        _settingPalette = true;
        try
        {
            if (option.Palette is { } palette)
            {
                ThemeMode.SelectedIndex = palette.ThemeMode == "light" ? 1 : 0;
                AccentColorPicker.Color = Color.Parse(palette.AccentColor);
                TextColorPicker.Color = Color.Parse(palette.TextColor);
                HighlightColorPicker.Color = Color.Parse(palette.HighlightColor);
                BackgroundColorPicker.Color = Color.Parse(palette.BackgroundColor);
            }
            UpdateThemeActions();
        }
        finally { _settingPalette = false; }
    }

    private void ThemeMode_Changed(object? sender, SelectionChangedEventArgs e) => ThemePaletteChanged();
    private void ThemeColor_Changed(object? sender, ColorChangedEventArgs e) => ThemePaletteChanged();

    private void ThemePaletteChanged()
    {
        if (_reloading || _settingPalette || ThemePresetMode == null || AccentColorPicker == null) return;
        _settingPalette = true;
        try
        {
            var palette = DraftPalette();
            ThemePresetMode.SelectedItem = ThemePresetMode.Items.OfType<ThemeOption>().FirstOrDefault(p => p.Palette == palette)
                ?? ThemePresetMode.Items.OfType<ThemeOption>().FirstOrDefault();
            // Keep the typed name so a customized saved palette can be updated under the same name.
            DeleteThemeButton.IsVisible = (ThemePresetMode.SelectedItem as ThemeOption)?.SavedName != null;
        }
        finally { _settingPalette = false; }
    }

    private void SaveThemePreset_Click(object? sender, RoutedEventArgs e)
    {
        var name = ThemePresetName.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name)) { AppearanceStatus.Text = Localization.Get("ThemeNameRequired"); ThemePresetName.Focus(); return; }
        try
        {
            UserConfiguration.SaveThemePreset(new(name, DraftPalette()));
            _watcher.AcceptCurrent();
            ReloadThemePresets(name);
            AppearanceStatus.Text = Localization.Get("ThemePresetSaved");
        }
        catch { AppearanceStatus.Text = Localization.Get("ThemePresetSaveError"); }
    }

    private void DeleteThemePreset_Click(object? sender, RoutedEventArgs e)
    {
        if (ThemePresetMode.SelectedItem is not ThemeOption { SavedName: { } name }) return;
        try
        {
            UserConfiguration.DeleteThemePreset(name);
            _watcher.AcceptCurrent();
            ReloadThemePresets();
            AppearanceStatus.Text = Localization.Get("ThemePresetDeleted");
        }
        catch { AppearanceStatus.Text = Localization.Get("PreferencesSaveError"); }
    }

    private void RefreshLanguage() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        // ComboBox caches the selected item's presentation. Re-select after
        // translated resources update so it cannot retain the previous language.
        _reloading = true;
        try
        {
            foreach (var status in new[] { AppearanceStatus, LyricsStatus, StatusText })
            {
                var entry = Localization.Catalog.FirstOrDefault(pair =>
                    pair.Value.English == status.Text || pair.Value.Chinese == status.Text);
                if (entry.Key != null) status.Text = Localization.Get(entry.Key);
            }
            foreach (var combo in new[] { PresetMode, IntegrationMode, ThemeMode, LanguageMode, UiScaleMode, SearchStrategyMode, MatchMode, PreferredLyricSourceMode })
            { var index = combo.SelectedIndex; combo.SelectedIndex = -1; combo.SelectedIndex = index; }
            var name = ThemePresetName.Text;
            ReloadThemePresets((ThemePresetMode.SelectedItem as ThemeOption)?.SavedName);
            ThemePresetName.Text = name;
            LanguageMode.SelectedIndex = UserConfiguration.LoadLanguage() switch { "zh-CN" => 1, "en" => 2, _ => 0 };
            VersionText.Text = Localization.Format("Version", ApplicationInfo.Version);
            UpdateTokenHint();
            RenderMacEnvironment();
            SetYesPlayMusicStatus(_yesPlayMusicStatusKey);
            SettingsTabs_SelectionChanged(this, null!);
        }
        catch { StatusText.Text = Localization.Get("ConfigReadError"); }
        finally { _reloading = false; }
    });

    private void RefreshWindowState()
    {
        var current = AppearancePreferences.Current;
        if (_lastWindowState?.Locked != current.Locked) LockedMode.IsChecked = current.Locked;
        if (_lastWindowState?.Preset != current.Preset)
            PresetMode.SelectedIndex = current.Preset switch
            { "compact" => 1, "focus" => 2, "portrait" => 3, "fullscreen" => 4, _ => 0 };
        if (!_savingScale && _lastWindowState?.UiScale != current.UiScale) LoadScale(current.UiScale);
        _lastWindowState = current;
    }

    private void ResetAppearanceButton_Click(object? sender, RoutedEventArgs e)
    {
        try { AppearancePreferences.Save(new("classic", false, true, true)); ReloadSettings(); }
        catch { AppearanceStatus.Text = Localization.Get("AppearanceError"); }
    }

    private void SettingsSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (SettingsTabs == null || SearchEmpty == null) return;
        var query = SettingsSearch.Text?.Trim() ?? "";
        var tabs = SettingsTabs.Items.OfType<TabItem>().ToList();
        foreach (var tab in tabs)
        {
            if (tab == MacEnvironmentTab && !OperatingSystem.IsMacOS()) { tab.IsVisible = false; continue; }
            var tags = (tab.Tag?.ToString() ?? "").Split(' ');
            var searchable = string.Join(" ", tags.Select(tag => Localization.Catalog.TryGetValue(tag, out var value)
                ? $"{tag} {value.English} {value.Chinese}" : tag));
            tab.IsVisible = query.Length == 0 || searchable.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (tab.Header?.ToString()?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
        }
        SearchEmpty.IsVisible = !tabs.Any(tab => tab.IsVisible);
        if (SettingsTabs.SelectedItem is not TabItem { IsVisible: true })
            SettingsTabs.SelectedItem = tabs.FirstOrDefault(tab => tab.IsVisible);
        if (query.Length > 0 && SettingsTabs.SelectedItem is TabItem selected)
        {
            var terms = Localization.Catalog.Where(pair => pair.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                || pair.Value.English.Contains(query, StringComparison.OrdinalIgnoreCase)
                || pair.Value.Chinese.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(pair => Localization.Get(pair.Key)).Append(query).ToArray();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // A delayed search must not scroll an old page after the user
                // has clicked another navigation item or changed the query.
                if (SettingsTabs.SelectedItem != selected || SettingsSearch.Text?.Trim() != query
                    || selected.Content is not Control page) return;
                var match = page.GetLogicalDescendants().OfType<Control>().FirstOrDefault(control =>
                {
                    var text = control is TextBlock label ? label.Text : control is ContentControl { Content: string content } ? content : null;
                    return text != null && terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
                });
                if (selected == PlayerConnectionsTab && match != null)
                {
                    var card = match.GetLogicalAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("card"));
                    (card ?? match).BringIntoView();
                }
                else match?.BringIntoView();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void SearchBoxHost_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(SearchBoxHost).Properties.IsLeftButtonPressed)
            SettingsSearch.Focus();
    }

    private void ReleaseSearchFocus()
    {
        if (SettingsSearch.IsKeyboardFocusWithin) FocusManager?.ClearFocus();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Source is Avalonia.Visual source && source != SettingsSearch
            && !source.GetVisualAncestors().Contains(SettingsSearch)) ReleaseSearchFocus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SettingsSearch.IsKeyboardFocusWithin)
        {
            ReleaseSearchFocus();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void RepositoryButton_Click(object? sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/zzxzzk115/OmniLyrics") { UseShellExecute = true }); }
        catch { }
    }
}
