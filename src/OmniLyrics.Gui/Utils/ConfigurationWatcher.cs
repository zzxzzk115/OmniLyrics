using System;
using System.IO;
using Avalonia.Threading;
using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Gui.Utils;

/// <summary>Debounces both in-place writes and editor-style atomic replacements.</summary>
public sealed class ConfigurationWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _disposed;
    private bool _invalid;
    private string? _lastText;
    public event Action? Changed;
    public event Action? Invalid;

    public ConfigurationWatcher()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(UserConfiguration.DirectoryPath);
        else Directory.CreateDirectory(UserConfiguration.DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try { _lastText = UserConfiguration.ReadConfigurationText(); } catch { }
        _watcher = new(UserConfiguration.DirectoryPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += (_, e) => { if (Relevant(e.Name) || Relevant(e.OldName)) Schedule(); };
        _watcher.Error += (_, _) => Schedule();
        _debounce.Tick += (_, _) => Check();
        _watcher.EnableRaisingEvents = true;
    }

    private static bool Relevant(string? name) => name is "config.json" or "cider-token";
    private void OnFileChanged(object sender, FileSystemEventArgs e) { if (Relevant(e.Name)) Schedule(); }
    private void Schedule() => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed) return;
        _debounce.Stop(); _debounce.Start();
    });

    private void Check()
    {
        _debounce.Stop();
        if (_disposed) return;
        try
        {
            // A deletion may be the middle of an editor's replace operation.
            if (!File.Exists(UserConfiguration.SettingsPath)) return;
            var text = UserConfiguration.ReadConfigurationText();
            if (text == _lastText && !_invalid) return;
            UserConfiguration.ValidateConfigurationText(text);
            _lastText = text;
            _invalid = false;
            Changed?.Invoke();
        }
        catch { _invalid = true; Invalid?.Invoke(); }
    }

    public void AcceptCurrent()
    {
        var text = UserConfiguration.ReadConfigurationText();
        UserConfiguration.ValidateConfigurationText(text);
        _lastText = text;
        _invalid = false;
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.Dispose();
        _debounce.Stop();
    }
}
