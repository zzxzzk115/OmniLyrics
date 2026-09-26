using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OmniLyrics.Backends.Mac;
using OmniLyrics.Core;

namespace OmniLyrics.Gui;

public partial class SettingsWindow
{
    private readonly CancellationTokenSource _macLifetime = new();
    private CancellationTokenSource? _macInstall;
    private MacEnvironmentStatus? _macStatus;
    private bool _macBusy;
    private Task? _macRefreshTask;

    private void InitializeMacEnvironment()
    {
        MacEnvironmentTab.IsVisible = OperatingSystem.IsMacOS();
        Closed += (_, _) => { _macLifetime.Cancel(); _macInstall?.Cancel(); };
    }

    public void ShowMacEnvironment()
    {
        if (!OperatingSystem.IsMacOS()) return;
        SettingsSearch.Text = "";
        SettingsTabs.SelectedItem = MacEnvironmentTab;
    }

    private Task RefreshMacEnvironmentAsync() => _macRefreshTask is { IsCompleted: false }
        ? _macRefreshTask : _macRefreshTask = RefreshMacEnvironmentCoreAsync();

    private async Task RefreshMacEnvironmentCoreAsync()
    {
        if (!OperatingSystem.IsMacOS() || _macBusy || _macLifetime.IsCancellationRequested) return;
        _macBusy = true; MacRefreshButton.IsEnabled = MacInstallButton.IsEnabled = false;
        MacNativeStatus.Text = Localization.Get("MacChecking");
        try { _macStatus = await MacEnvironment.ProbeAsync(_macLifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { MacInstallStatus.Text = error.Message; }
        finally { _macBusy = false; RenderMacEnvironment(); }
    }

    private void RenderMacEnvironment()
    {
        MacRefreshButton.IsEnabled = !_macBusy;
        if (_macStatus == null) return;
        MacNativeStatus.Text = Localization.Get(_macStatus.NativeAvailable switch
        { true => "MacNativeReady", false => "MacNativeUnavailable", _ => "MacNativePending" });
        MacMediaStatus.Text = Localization.Get(_macStatus.MediaControlDisabled ? "MacFallbackDisabled" :
            _macStatus.MediaControlAvailable ? "MacMediaReady" : _macStatus.MediaControlPath == null ? "MacMediaMissing" : "MacMediaFailed");
        MacMediaPath.Text = _macStatus.MediaControlPath ?? "";
        MacBrewStatus.Text = _macStatus.BrewPath == null ? Localization.Get("MacBrewMissing") : "Homebrew: " + _macStatus.BrewPath;
        MacRefreshButton.IsEnabled = !_macBusy;
        MacInstallButton.IsEnabled = !_macBusy && _macStatus.BrewPath != null && !_macStatus.MediaControlAvailable && !_macStatus.MediaControlDisabled;
        MacBrewWebsiteButton.IsVisible = _macStatus.BrewPath == null;
    }

    private async void MacRefresh_Click(object? sender, RoutedEventArgs e) => await RefreshMacEnvironmentAsync();
    private async void MacInstall_Click(object? sender, RoutedEventArgs e) => await InstallMacMediaControlAsync();
    private void MacCancelInstall_Click(object? sender, RoutedEventArgs e) => _macInstall?.Cancel();
    private void MacAutomation_Click(object? sender, RoutedEventArgs e) => OpenMacLink("x-apple.systempreferences:com.apple.preference.security?Privacy_Automation");
    private void MacBrewWebsite_Click(object? sender, RoutedEventArgs e) => OpenMacLink("https://brew.sh/");

    private void OpenMacLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) { MacInstallStatus.Text = error.Message; }
    }

    public async Task InstallMacMediaControlAsync()
    {
        if (!OperatingSystem.IsMacOS() || _macInstall != null) return;
        await RefreshMacEnvironmentAsync();
        if (_macBusy) return;
        if (_macStatus?.BrewPath == null || _macStatus.MediaControlDisabled || _macStatus.MediaControlAvailable) return;
        _macBusy = true;
        MacInstallButton.IsEnabled = MacRefreshButton.IsEnabled = false;
        MacCancelInstallButton.IsVisible = true;
        MacInstallLog.IsVisible = true; MacInstallLog.Text = "brew install media-control\n";
        MacInstallStatus.Text = Localization.Get("MacInstalling");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_macLifetime.Token);
        _macInstall = cancellation;
        try
        {
            var code = await MacEnvironment.InstallMediaControlAsync(_macStatus.BrewPath, line => Dispatcher.UIThread.Post(() =>
            {
                var text = (MacInstallLog.Text ?? "") + line + "\n";
                MacInstallLog.Text = text.Length > 24000 ? text[^24000..] : text;
                MacInstallLog.CaretIndex = MacInstallLog.Text.Length;
            }), cancellation.Token);
            _macStatus = await MacEnvironment.ProbeAsync(cancellation.Token);
            MacInstallStatus.Text = Localization.Get(code == 0 && _macStatus.MediaControlAvailable ? "MacInstallRestart" : "MacInstallFailed");
        }
        catch (OperationCanceledException) { MacInstallStatus.Text = Localization.Get("MacInstallCancelled"); }
        catch (Exception error) { MacInstallStatus.Text = Localization.Get("MacInstallFailed") + " " + error.Message; }
        finally
        {
            _macInstall = null; _macBusy = false;
            MacCancelInstallButton.IsVisible = false;
            RenderMacEnvironment();
        }
    }
}
