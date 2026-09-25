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

        }

        base.OnFrameworkInitializationCompleted();
    }
}
