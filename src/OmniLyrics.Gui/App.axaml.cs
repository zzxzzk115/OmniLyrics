using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OmniLyrics.Gui.Models;

namespace OmniLyrics.Gui;

public class App : Application
{
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Initialize windows
            _mainWindow = new MainWindow
            {
                IsVisible = true
            };
            _settingsWindow = new SettingsWindow
            {
                IsVisible = false
            };

            // Bind Tray view model
            var trayVm = new TrayViewModel(_mainWindow, _settingsWindow, desktop);
            DataContext = trayVm;
        }

        base.OnFrameworkInitializationCompleted();
    }
}