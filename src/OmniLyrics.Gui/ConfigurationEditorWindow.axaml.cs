using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Gui.Utils;

namespace OmniLyrics.Gui;

public partial class ConfigurationEditorWindow : Window
{
    private readonly ConfigurationWatcher _watcher;
    private string _loaded = "";
    public ConfigurationEditorWindow()
    {
        InitializeComponent();
        _ = new WindowScale(this, size => ClientSize = size, scrollWhenConstrained: true);
        ConfigurationPath.Text = UserConfiguration.SettingsPath;
        Reload();
        _watcher = new();
        _watcher.Changed += () =>
        {
            if (ConfigurationText.Text == _loaded) Reload();
            else EditorStatus.Text = Localization.Get("ConfigurationConflict");
        };
        Closed += (_, _) => _watcher.Dispose();
    }
    private void Reload()
    {
        try { _loaded = UserConfiguration.ReadConfigurationText(); ConfigurationText.Text = _loaded; EditorStatus.Text = ""; }
        catch { EditorStatus.Text = Localization.Get("ConfigReadError"); }
    }
    private void ReloadButton_Click(object? sender, RoutedEventArgs e) => Reload();
    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (UserConfiguration.ReadConfigurationText() != _loaded)
            { EditorStatus.Text = Localization.Get("ConfigurationConflict"); return; }
            UserConfiguration.SaveConfigurationText(ConfigurationText.Text ?? "");
            Reload(); EditorStatus.Text = Localization.Get("SettingsApplied");
        }
        catch { EditorStatus.Text = Localization.Get("ConfigurationInvalid"); }
    }
}
