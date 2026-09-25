using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using OmniLyrics.Gui.Utils;

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

    // === Header text ===
    public string ShowLyricsText =>
        _mainWindow.IsVisible ? "✓ Show Lyrics" : "Show Lyrics";

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