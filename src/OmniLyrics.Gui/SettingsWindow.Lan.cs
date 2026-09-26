using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Network;

namespace OmniLyrics.Gui;

public partial class SettingsWindow
{
    private readonly LanTrustStore _lanTrust = new();
    private CancellationTokenSource? _lanOperation;
    private LanSettings? _loadedLanSettings;
    private bool _checkingLanStatus;
    private string _lanStatusKey = "";
    private sealed record DeviceOption(string Id, string Label)
    { public override string ToString() => Label; }
    private void InitializeLan()
    {
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { if (IsVisible && SettingsTabs.SelectedItem == LanTab) _ = RefreshLanStatusAsync(); };
        timer.Start();
        Closed += (_, _) => { _lanOperation?.Cancel(); timer.Stop(); };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && !IsVisible)
            { _lanOperation?.Cancel(); LanInvitation.Text = ""; LanPairInput.Text = ""; LanInvitation.IsVisible = LanCopyInvite.IsVisible = false; }
        };
    }
    private void ReloadLan(bool preserveInput = false)
    {
        var settings = UserConfiguration.LoadLan();
        if (!preserveInput || _loadedLanSettings == null || LanEnabled.IsChecked == _loadedLanSettings.Enabled)
            LanEnabled.IsChecked = settings.Enabled;
        if (!preserveInput || _loadedLanSettings == null || LanName.Text == LanTrustStore.Name(_loadedLanSettings.DeviceName))
            LanName.Text = LanTrustStore.Name(settings.DeviceName);
        _loadedLanSettings = settings;
        var address = LanAddressChoice.SelectedItem as string;
        var addresses = LanAddress.LocalAddresses();
        LanAddressChoice.ItemsSource = addresses;
        LanAddressChoice.SelectedItem = addresses.Contains(address) ? address : addresses.FirstOrDefault();
        var selected = (LanPeers.SelectedItem as DeviceOption)?.Id ?? settings.SelectedDeviceId;
        var peers = _lanTrust.Peers();
        LanPeers.ItemsSource = peers.Select(p => new DeviceOption(p.Id, $"{p.Name} · {p.Host} · {Localization.Get(p.AllowControl ? "LanControl" : "LanReadOnly")}")).ToArray();
        LanPeers.SelectedItem = LanPeers.Items.OfType<DeviceOption>().FirstOrDefault(p => p.Id == selected) ?? LanPeers.Items.OfType<DeviceOption>().FirstOrDefault();
        LanGrants.ItemsSource = _lanTrust.Grants().Select(p => new DeviceOption(p.Id, $"{p.Name} · {Localization.Get(p.AllowControl ? "LanControl" : "LanReadOnly")}")).ToArray();
        LanGrants.SelectedIndex = LanGrants.ItemCount > 0 ? 0 : -1;
        LanCurrentSource.Text = settings.SelectedDeviceId == null ? Localization.Get("LanLocalSource")
            : Localization.Get("LanSource") + ": " + (peers.FirstOrDefault(p => p.Id == settings.SelectedDeviceId)?.Name ?? Localization.Get("LanOffline"));
        LanStatus.Text = _lanStatusKey.Length == 0 ? "" : Localization.Get(_lanStatusKey);
    }
    private async Task RefreshLanStatusAsync()
    {
        if (_checkingLanStatus) return;
        _checkingLanStatus = true;
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
            var server = UserConfiguration.LoadServer();
            var host = System.Net.IPAddress.TryParse(server.ListenAddress, out var address) && System.Net.IPAddress.IsLoopback(address) ? address.ToString() : "127.0.0.1";
            var status = await http.GetFromJsonAsync<LanSharingStatus>(new Uri(new UriBuilder("http", host, server.HttpPort).Uri, "sharing"));
            LanServiceStatus.Text = Localization.Get(status?.Enabled != true ? "LanSharingDisabled" : status.Running ? "LanSharingReady" : "LanSharingUnavailable");
        }
        catch { LanServiceStatus.Text = Localization.Get("LanStartService"); }
        finally { _checkingLanStatus = false; }
    }
    private void SetLanStatus(string key) { _lanStatusKey = key; LanStatus.Text = Localization.Get(key); }
    private void LanAction(Action action)
    {
        try { action(); _watcher.AcceptCurrent(); ReloadLan(true); SetLanStatus("SettingsApplied"); _ = RefreshLanStatusAsync(); }
        catch { SetLanStatus("LanFailed"); }
    }
    private void SaveLanSharing()
    {
        var settings = UserConfiguration.LoadLan();
        var proposed = settings with { Enabled = LanEnabled.IsChecked == true, DeviceName = LanName.Text?.Trim() ?? "" };
        if (proposed != settings) UserConfiguration.SaveLan(proposed);
        if (!proposed.Enabled) { _lanTrust.CancelInvitation(); ClearInvitation(); }
    }
    private void LanSave_Click(object? sender, RoutedEventArgs e) => LanAction(SaveLanSharing);
    private void LanInvite_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (LanAddressChoice.SelectedItem is not string host) { SetLanStatus("LanNoAddress"); return; }
            if (!UserConfiguration.LoadLan().Enabled) { SetLanStatus("LanEnableFirst"); return; }
            LanInvitation.Text = _lanTrust.CreateInvitation(host, UserConfiguration.LoadLan(), LanControlGrant.IsChecked == true);
            LanInvitation.IsVisible = LanCopyInvite.IsVisible = true;
            SetLanStatus("LanInviteHint");
        }
        catch { SetLanStatus("LanFailed"); }
    }
    private void ClearInvitation() { LanInvitation.Text = ""; LanInvitation.IsVisible = LanCopyInvite.IsVisible = false; }
    private void LanCancelInvite_Click(object? sender, RoutedEventArgs e) => LanAction(() => { _lanTrust.CancelInvitation(); ClearInvitation(); });
    private async void LanCopy_Click(object? sender, RoutedEventArgs e)
    {
        try { if (Clipboard != null) await Clipboard.SetTextAsync(LanInvitation.Text); }
        catch { SetLanStatus("LanFailed"); }
    }
    private async Task LanOperation(Func<CancellationToken, Task> action)
    {
        if (_lanOperation != null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _lanOperation = cancellation;
        LanPairButton.IsEnabled = LanDiscoverButton.IsEnabled = false;
        SetLanStatus("LanWorking");
        try { await action(cancellation.Token); _watcher.AcceptCurrent(); ReloadLan(true); }
        catch (OperationCanceledException) { SetLanStatus("AuthorizationCancelled"); }
        catch { SetLanStatus("LanFailed"); }
        finally { _lanOperation = null; LanPairButton.IsEnabled = LanDiscoverButton.IsEnabled = true; }
    }
    private async void LanDiscover_Click(object? sender, RoutedEventArgs e) => await LanOperation(async token =>
    {
        var devices = (await LanDiscovery.FindAsync(UserConfiguration.LoadLan().DiscoveryPort, token)).Where(d => d.Id != _lanTrust.DeviceId).ToArray();
        LanFound.ItemsSource = devices;
        SetLanStatus(devices.Length == 0 ? "LanNoneFound" : "LanPairHint");
    });
    private async void LanPair_Click(object? sender, RoutedEventArgs e) => await LanOperation(async token =>
    {
        var invitation = LanPairInput.Text?.Trim() ?? "";
        LanPairInput.Text = "";
        await LanClient.PairAsync(invitation, LanTrustStore.Name(UserConfiguration.LoadLan().DeviceName), _lanTrust, token);
        SetLanStatus("LanPaired");
    });
    private void LanSource_Click(object? sender, RoutedEventArgs e) => LanAction(() =>
    {
        if (LanPeers.SelectedItem is DeviceOption peer)
            UserConfiguration.SaveLan(UserConfiguration.LoadLan() with { SelectedDeviceId = peer.Id });
    });
    private void LanLocal_Click(object? sender, RoutedEventArgs e) => LanAction(() =>
        UserConfiguration.SaveLan(UserConfiguration.LoadLan() with { SelectedDeviceId = null }));
    private void LanForget_Click(object? sender, RoutedEventArgs e) => LanAction(() =>
    {
        if (LanPeers.SelectedItem is not DeviceOption peer) return;
        _lanTrust.Forget(peer.Id);
        var settings = UserConfiguration.LoadLan();
        if (settings.SelectedDeviceId == peer.Id) UserConfiguration.SaveLan(settings with { SelectedDeviceId = null });
    });
    private void LanRevoke_Click(object? sender, RoutedEventArgs e) => LanAction(() =>
    { if (LanGrants.SelectedItem is DeviceOption peer) _lanTrust.Revoke(peer.Id); });
    private void LanRefresh_Click(object? sender, RoutedEventArgs e) => LanAction(() => { });
}
