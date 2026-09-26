using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Favorites;

namespace OmniLyrics.Gui;

public partial class SettingsWindow
{
    private CancellationTokenSource? _spotifyLogin, _yesPlayMusicLogin, _yesPlayMusicCheck;
    private string _yesPlayMusicStatusKey = "YesPlayMusicLocalFirst";
    private Bitmap? _loginQr;
    private string _loadedSpotifyClientId = "";
    private void InitializeFavorites()
    {
        AppleMusicFavoritesCard.IsVisible = OperatingSystem.IsMacOS();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) CancelFavoriteLogins(); };
        Closed += (_, _) => CancelFavoriteLogins();
    }
    private void CancelFavoriteLogins() { _spotifyLogin?.Cancel(); _yesPlayMusicLogin?.Cancel(); _yesPlayMusicCheck?.Cancel(); }
    private void ReloadFavorites(bool preserveInput)
    {
        var id = UserConfiguration.LoadSpotifyClientId();
        if (!preserveInput || SpotifyClientIdInput.Text == _loadedSpotifyClientId) SpotifyClientIdInput.Text = id;
        _loadedSpotifyClientId = id;
        if (_spotifyLogin == null) SpotifyLoginStatus.Text = Localization.Get(UserConfiguration.ReadSpotifyTokens() == null ? "AuthorizationNotSaved" : "AuthorizationSaved");
        if (_yesPlayMusicLogin == null && _yesPlayMusicCheck == null)
            SetYesPlayMusicStatus(string.IsNullOrEmpty(UserConfiguration.ReadYesPlayMusicCookie()) ? "YesPlayMusicLocalFirst" : "AuthorizationSaved");
        UpdateYesPlayMusicButtons();
    }
    private async void SpotifySignIn_Click(object? sender, RoutedEventArgs e)
    {
        if (_spotifyLogin != null) return;
        using var cancellation = new CancellationTokenSource();
        _spotifyLogin = cancellation;
        SpotifySignInButton.IsEnabled = SpotifyClientIdInput.IsEnabled = false;
        SpotifyCancelButton.IsVisible = true;
        SpotifyLoginStatus.Text = Localization.Get("SpotifyWaiting");
        try
        {
            using var authorization = new SpotifyAuthorization();
            await authorization.SignInAsync(SpotifyClientIdInput.Text ?? "", uri => Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!await Launcher.LaunchUriAsync(uri)) throw new InvalidOperationException(Localization.Get("BrowserOpenFailed"));
            }), cancellation.Token);
            _loadedSpotifyClientId = UserConfiguration.LoadSpotifyClientId();
            _watcher.AcceptCurrent();
            SpotifyLoginStatus.Text = Localization.Get("AuthorizationSaved");
        }
        catch (OperationCanceledException) { SpotifyLoginStatus.Text = Localization.Get("AuthorizationCancelled"); }
        catch { SpotifyLoginStatus.Text = Localization.Get("SpotifyAuthorizationFailed"); }
        finally
        {
            _spotifyLogin = null;
            SpotifySignInButton.IsEnabled = SpotifyClientIdInput.IsEnabled = true;
            SpotifyCancelButton.IsVisible = false;
        }
    }
    private void SpotifyCancel_Click(object? sender, RoutedEventArgs e) => _spotifyLogin?.Cancel();
    private void SpotifyDisconnect_Click(object? sender, RoutedEventArgs e)
    {
        _spotifyLogin?.Cancel();
        try { UserConfiguration.ClearSpotifyTokens(); SpotifyLoginStatus.Text = Localization.Get("AuthorizationNotSaved"); }
        catch { SpotifyLoginStatus.Text = Localization.Get("PreferencesSaveError"); }
    }
    private void SetYesPlayMusicStatus(string key)
    {
        _yesPlayMusicStatusKey = key;
        YesPlayMusicLoginStatus.Text = Localization.Get(key);
    }
    private void UpdateYesPlayMusicButtons()
    {
        var busy = _yesPlayMusicLogin != null || _yesPlayMusicCheck != null;
        YesPlayMusicCheckButton.IsEnabled = YesPlayMusicSignInButton.IsEnabled = !busy;
        YesPlayMusicDisconnectButton.IsEnabled = !busy;
        YesPlayMusicDisconnectButton.IsVisible = !string.IsNullOrEmpty(UserConfiguration.ReadYesPlayMusicCookie());
        YesPlayMusicCancelButton.IsVisible = _yesPlayMusicLogin != null;
    }
    private async void YesPlayMusicCheck_Click(object? sender, RoutedEventArgs e)
    {
        if (_yesPlayMusicLogin != null || _yesPlayMusicCheck != null) return;
        using var cancellation = new CancellationTokenSource();
        _yesPlayMusicCheck = cancellation;
        UpdateYesPlayMusicButtons();
        SetYesPlayMusicStatus("YesPlayMusicChecking");
        try
        {
            using var api = new YesPlayMusicFavorites();
            var status = await api.CheckAuthorizationAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            SetYesPlayMusicStatus(status switch
            {
                YesPlayMusicAuthorizationState.Authorized => "YesPlayMusicAuthorized",
                YesPlayMusicAuthorizationState.LocalSession => "YesPlayMusicLocalConnected",
                YesPlayMusicAuthorizationState.Expired => "YesPlayMusicAuthorizationExpired",
                _ => "YesPlayMusicLocalNeedsLogin"
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { SetYesPlayMusicStatus(string.IsNullOrEmpty(UserConfiguration.ReadYesPlayMusicCookie()) ? "YesPlayMusicLocalFirst" : "AuthorizationSaved"); }
        catch { SetYesPlayMusicStatus("YesPlayMusicUnavailable"); }
        finally { _yesPlayMusicCheck = null; UpdateYesPlayMusicButtons(); }
    }
    private async void YesPlayMusicSignIn_Click(object? sender, RoutedEventArgs e)
    {
        if (_yesPlayMusicLogin != null || _yesPlayMusicCheck != null) return;
        using var cancellation = new CancellationTokenSource();
        _yesPlayMusicLogin = cancellation;
        UpdateYesPlayMusicButtons();
        SetYesPlayMusicStatus("YesPlayMusicCreatingQr");
        try
        {
            using var authorization = new YesPlayMusicFavorites();
            await authorization.SignInAsync(bytes => Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                _loginQr?.Dispose();
                using var stream = new MemoryStream(bytes);
                _loginQr = new Bitmap(stream);
                YesPlayMusicQr.Source = _loginQr;
                YesPlayMusicQrPanel.IsVisible = true;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!cancellation.IsCancellationRequested && YesPlayMusicQrPanel.IsVisible)
                        YesPlayMusicCard.BringIntoView();
                }, DispatcherPriority.Loaded);
            }).GetTask(), cancellation.Token, status => Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                SetYesPlayMusicStatus(status == YesPlayMusicQrStatus.AwaitingConfirmation ? "YesPlayMusicConfirmQr" : "YesPlayMusicWaiting");
            }).GetTask());
            SetYesPlayMusicStatus("YesPlayMusicAuthorized");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { SetYesPlayMusicStatus("AuthorizationCancelled"); }
        catch (TimeoutException) { SetYesPlayMusicStatus("YesPlayMusicQrExpired"); }
        catch { SetYesPlayMusicStatus("YesPlayMusicLoginFailed"); }
        finally
        {
            _yesPlayMusicLogin = null;
            YesPlayMusicQrPanel.IsVisible = false;
            YesPlayMusicQr.Source = null;
            _loginQr?.Dispose(); _loginQr = null;
            UpdateYesPlayMusicButtons();
        }
    }
    private void YesPlayMusicCancel_Click(object? sender, RoutedEventArgs e) => _yesPlayMusicLogin?.Cancel();
    private void YesPlayMusicDisconnect_Click(object? sender, RoutedEventArgs e)
    {
        if (_yesPlayMusicLogin != null || _yesPlayMusicCheck != null) return;
        try { UserConfiguration.ClearYesPlayMusicCookie(); SetYesPlayMusicStatus("YesPlayMusicSignedOut"); }
        catch { SetYesPlayMusicStatus("PreferencesSaveError"); }
        UpdateYesPlayMusicButtons();
    }
}
