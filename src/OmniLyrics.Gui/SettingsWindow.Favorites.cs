using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Favorites;

namespace OmniLyrics.Gui;

public partial class SettingsWindow
{
    private CancellationTokenSource? _spotifyLogin, _yesPlayMusicLogin;
    private Bitmap? _loginQr;
    private string _loadedSpotifyClientId = "";
    private void InitializeFavorites()
    {
        AppleMusicFavoritesCard.IsVisible = OperatingSystem.IsMacOS();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) CancelFavoriteLogins(); };
        Closed += (_, _) => CancelFavoriteLogins();
    }
    private void CancelFavoriteLogins() { _spotifyLogin?.Cancel(); _yesPlayMusicLogin?.Cancel(); }
    private void ReloadFavorites(bool preserveInput)
    {
        var id = UserConfiguration.LoadSpotifyClientId();
        if (!preserveInput || SpotifyClientIdInput.Text == _loadedSpotifyClientId) SpotifyClientIdInput.Text = id;
        _loadedSpotifyClientId = id;
        if (_spotifyLogin == null) SpotifyLoginStatus.Text = Localization.Get(UserConfiguration.ReadSpotifyTokens() == null ? "AuthorizationNotSaved" : "AuthorizationSaved");
        if (_yesPlayMusicLogin == null) YesPlayMusicLoginStatus.Text = Localization.Get(string.IsNullOrEmpty(UserConfiguration.ReadYesPlayMusicCookie()) ? "YesPlayMusicLocalFirst" : "AuthorizationSaved");
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
    private async void YesPlayMusicCheck_Click(object? sender, RoutedEventArgs e)
    {
        YesPlayMusicCheckButton.IsEnabled = false;
        try
        {
            using var api = new YesPlayMusicFavorites();
            YesPlayMusicLoginStatus.Text = Localization.Get(await api.HasLocalSessionAsync() ? "YesPlayMusicLocalConnected" : "YesPlayMusicLocalNeedsLogin");
        }
        catch { YesPlayMusicLoginStatus.Text = Localization.Get("YesPlayMusicUnavailable"); }
        finally { YesPlayMusicCheckButton.IsEnabled = true; }
    }
    private async void YesPlayMusicSignIn_Click(object? sender, RoutedEventArgs e)
    {
        if (_yesPlayMusicLogin != null) return;
        using var cancellation = new CancellationTokenSource();
        _yesPlayMusicLogin = cancellation;
        YesPlayMusicSignInButton.IsEnabled = false;
        YesPlayMusicCancelButton.IsVisible = true;
        YesPlayMusicLoginStatus.Text = Localization.Get("YesPlayMusicWaiting");
        try
        {
            using var authorization = new YesPlayMusicFavorites();
            await authorization.SignInAsync(bytes => Dispatcher.UIThread.InvokeAsync(() =>
            {
                _loginQr?.Dispose();
                using var stream = new MemoryStream(bytes);
                _loginQr = new Bitmap(stream);
                YesPlayMusicQr.Source = _loginQr;
                YesPlayMusicQr.IsVisible = true;
            }).GetTask(), cancellation.Token);
            YesPlayMusicLoginStatus.Text = Localization.Get("AuthorizationSaved");
        }
        catch (OperationCanceledException) { YesPlayMusicLoginStatus.Text = Localization.Get("AuthorizationCancelled"); }
        catch { YesPlayMusicLoginStatus.Text = Localization.Get("YesPlayMusicLoginFailed"); }
        finally
        {
            _yesPlayMusicLogin = null;
            YesPlayMusicSignInButton.IsEnabled = true;
            YesPlayMusicCancelButton.IsVisible = YesPlayMusicQr.IsVisible = false;
            YesPlayMusicQr.Source = null;
            _loginQr?.Dispose(); _loginQr = null;
        }
    }
    private void YesPlayMusicCancel_Click(object? sender, RoutedEventArgs e) => _yesPlayMusicLogin?.Cancel();
    private void YesPlayMusicDisconnect_Click(object? sender, RoutedEventArgs e)
    {
        _yesPlayMusicLogin?.Cancel();
        try { UserConfiguration.ClearYesPlayMusicCookie(); YesPlayMusicLoginStatus.Text = Localization.Get("YesPlayMusicLocalFirst"); }
        catch { YesPlayMusicLoginStatus.Text = Localization.Get("PreferencesSaveError"); }
    }
}
