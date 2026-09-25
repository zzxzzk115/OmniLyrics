using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OmniLyrics.Gui.Models;
using OmniLyrics.Gui.Utils;
using OmniLyrics.Core;
using Avalonia.Threading;

namespace OmniLyrics.Gui;

public class App : Application
{
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private LinuxTrayCompatibility? _trayCompatibility;

    private void Settings_Click(object? sender, System.EventArgs e) => ShowSettings(false);
    private void About_Click(object? sender, System.EventArgs e) => ShowSettings(true);
    private void ShowSettings(bool about)
    {
        var settings = _settingsWindow ??= (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as SettingsWindow
            ?? new SettingsWindow { KeepAlive = true };
        settings.Show();
        settings.Activate();
        if (about) settings.OpenAbout();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyLocalization();
        ThemeManager.Apply(AppearancePreferences.Current);
        AppearancePreferences.Changed += () => ThemeManager.Apply(AppearancePreferences.Current);
        Localization.Changed += () => Dispatcher.UIThread.Post(ApplyLocalization);
    }

    private void ApplyLocalization()
    {
        foreach (var key in Localization.Catalog.Keys) Resources[key] = Localization.Get(key);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        MacApplicationIcon.Apply();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (System.Array.IndexOf(desktop.Args ?? [], "--settings") >= 0)
            {
                Avalonia.Controls.TrayIcon.GetIcons(this)?.Clear();
                desktop.MainWindow = new SettingsWindow();
            }
            else
            {
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                _mainWindow = new MainWindow();
                _settingsWindow = new SettingsWindow { KeepAlive = true };
                desktop.MainWindow = _mainWindow;
                DataContext = new TrayViewModel(_mainWindow, _settingsWindow, desktop);
                var icons = Avalonia.Controls.TrayIcon.GetIcons(this);
                if (System.OperatingSystem.IsLinux() && icons is { Count: > 0 })
                    _trayCompatibility = new LinuxTrayCompatibility(icons[0]);
                desktop.Exit += (_, _) =>
                {
                    _trayCompatibility?.Dispose();
                    (_mainWindow.DataContext as LyricsViewModel)?.Dispose();
                };
            }

            if (System.OperatingSystem.IsMacOS() && desktop.MainWindow is { } owner)
            {
                var setupCancellation = new System.Threading.CancellationTokenSource();
                desktop.Exit += (_, _) => setupCancellation.Cancel();
                owner.Opened += async (_, _) => await MacStartupSetup.CheckAsync(owner,
                    () => _settingsWindow ??= owner as SettingsWindow ?? new SettingsWindow { KeepAlive = true }, setupCancellation.Token);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
