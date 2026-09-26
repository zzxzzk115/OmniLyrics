using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using OmniLyrics.Gui.Utils;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Gui.Models;

public class TrayViewModel : INotifyPropertyChanged
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;

    private readonly MainWindow _mainWindow;
    private readonly SettingsWindow _settingsWindow;

    public TrayViewModel(
        MainWindow mainWindow,
        SettingsWindow settingsWindow,
        IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _mainWindow = mainWindow;
        _settingsWindow = settingsWindow;
        _lifetime = lifetime;

        ToggleLyricsCommand = new RelayCommand(ToggleLyrics);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        QuitCommand = new RelayCommand(Quit);
        RecoverInterfaceCommand = new RelayCommand(() =>
        {
            Change(s => s with { UiScale = 1 });
            OpenSettings();
            _settingsWindow.RecoverWindowPlacement();
        });
        SystemScaleCommand = Scale(null);
        Scale100Command = Scale(1); Scale125Command = Scale(1.25); Scale150Command = Scale(1.5); Scale200Command = Scale(2);
        ClassicCommand = Layout("classic"); CompactCommand = Layout("compact"); FocusCommand = Layout("focus");
        PortraitCommand = Layout("portrait"); FullscreenCommand = Layout("fullscreen");
        DarkCommand = new RelayCommand(() => Change(s => s with { ThemeMode = "dark" }));
        LightCommand = new RelayCommand(() => Change(s => s with { ThemeMode = "light" }));
        BlurCommand = new RelayCommand(() => Change(s => s with { UseBlur = !s.UseBlur }));
        TranslationCommand = new RelayCommand(() => Change(s => s with { ShowTranslation = !s.ShowTranslation }));
        LargerTextCommand = new RelayCommand(() => Change(s => s with { FontSize = Math.Min(72, s.FontSize + 2) }));
        SmallerTextCommand = new RelayCommand(() => Change(s => s with { FontSize = Math.Max(16, s.FontSize - 2) }));
        ToggleLockCommand = new RelayCommand(() =>
        {
            try { AppearancePreferences.ToggleLock(); } catch { }
        });
        Localization.Changed += OnLanguageChanged;
        AppearancePreferences.Changed += OnLanguageChanged;
        _lifetime.Exit += (_, _) => { Localization.Changed -= OnLanguageChanged; AppearancePreferences.Changed -= OnLanguageChanged; };

        _mainWindow.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == nameof(MainWindow.IsVisible))
                OnPropertyChanged(nameof(ShowLyricsText));
        };
    }

    // === Commands exposed to XAML ===
    public ICommand ToggleLyricsCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand QuitCommand { get; }
    public ICommand ToggleLockCommand { get; }
    public ICommand RecoverInterfaceCommand { get; }
    public ICommand SystemScaleCommand { get; }
    public ICommand Scale100Command { get; }
    public ICommand Scale125Command { get; }
    public ICommand Scale150Command { get; }
    public ICommand Scale200Command { get; }
    public ICommand ClassicCommand { get; }
    public ICommand CompactCommand { get; }
    public ICommand FocusCommand { get; }
    public ICommand PortraitCommand { get; }
    public ICommand FullscreenCommand { get; }
    public ICommand DarkCommand { get; }
    public ICommand LightCommand { get; }
    public ICommand BlurCommand { get; }
    public ICommand TranslationCommand { get; }
    public ICommand LargerTextCommand { get; }
    public ICommand SmallerTextCommand { get; }
    private void Change(Func<AppearanceSettings, AppearanceSettings> change)
    {
        try { AppearancePreferences.Save(change(AppearancePreferences.Current)); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ArgumentException) { }
    }
    private ICommand Scale(double? value) => new RelayCommand(() => Change(s => s with { UiScale = value }));
    private ICommand Layout(string value) => new RelayCommand(() => Change(s => s with { Preset = value }));
    private static string Selected(string text, bool selected) => (selected ? "✓ " : "") + text;
    public string SystemScaleText => Selected(Localization.Get("SystemScale"), AppearancePreferences.Current.UiScale == null);
    public string Scale100Text => Selected("100%", AppearancePreferences.Current.UiScale == 1);
    public string Scale125Text => Selected("125%", AppearancePreferences.Current.UiScale == 1.25);
    public string Scale150Text => Selected("150%", AppearancePreferences.Current.UiScale == 1.5);
    public string Scale200Text => Selected("200%", AppearancePreferences.Current.UiScale == 2);
    public string ClassicText => Selected(Localization.Get("ClassicPreset"), AppearancePreferences.Current.Preset == "classic");
    public string CompactText => Selected(Localization.Get("CompactPreset"), AppearancePreferences.Current.Preset == "compact");
    public string FocusText => Selected(Localization.Get("FocusPreset"), AppearancePreferences.Current.Preset == "focus");
    public string PortraitText => Selected(Localization.Get("PortraitPreset"), AppearancePreferences.Current.Preset == "portrait");
    public string FullscreenText => Selected(Localization.Get("FullscreenPreset"), AppearancePreferences.Current.Preset == "fullscreen");
    public string DarkText => Selected(Localization.Get("DarkTheme"), AppearancePreferences.Current.ThemeMode == "dark");
    public string LightText => Selected(Localization.Get("LightTheme"), AppearancePreferences.Current.ThemeMode == "light");
    public string BlurText => Selected(Localization.Get("UseBlur"), AppearancePreferences.Current.UseBlur);
    public string TranslationText => Selected(Localization.Get("TrayTranslation"), AppearancePreferences.Current.ShowTranslation);
    public string LockText => Localization.Get(AppearancePreferences.Current.Locked ? "UnlockWindow" : "LockWindow");

    // === Header text ===
    public string ShowLyricsText =>
        (_mainWindow.IsVisible ? "✓ " : "") + Localization.Get("ShowLyrics");

    private void OnLanguageChanged() => OnPropertyChanged(null);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ToggleLyrics()
    {
        if (_mainWindow.IsVisible)
            _mainWindow.Hide();
        else
            _mainWindow.Show();

        OnPropertyChanged(nameof(ShowLyricsText));
    }

    public void OpenSettings()
    {
        if (!_settingsWindow.IsVisible)
            _settingsWindow.Show();

        _settingsWindow.WindowState = WindowState.Normal;

        // Try Avalonia way
        _settingsWindow.Activate();

        // WIN32 override if needed
        if (OperatingSystem.IsWindows())
        {
            IntPtr? handle = _settingsWindow.TryGetPlatformHandle()?.Handle;
            if (handle != null && handle != IntPtr.Zero)
                Win32FocusHelper.ForceToForeground(handle.Value);
        }
    }

    public void Quit()
    {
        _lifetime.Shutdown();
    }

    protected void OnPropertyChanged([CallerMemberName] string? p = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
