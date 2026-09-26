using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Network;

namespace OmniLyrics.Core;

public sealed record LanSharingStatus(bool Enabled, bool Running, string? Error);

public partial class SharedPlayerSession
{
    private OmniLyrics.Web.WebApiServer? _lanWeb;
    private LanSettings _sharingSettings = new();
    private string? _lanHostError;
    private long _lanRetryAt;
    private async Task StartLanServiceAsync(LanSettings settings, CancellationToken token)
    {
        if (!settings.Enabled || _lanWeb?.IsRunning == true || Environment.TickCount64 < _lanRetryAt) return;
        if (_lanWeb != null) { await _lanWeb.DisposeAsync(); _lanWeb = null; }
        _lanRetryAt = Environment.TickCount64 + 5000;
        try
        {
            _lanWeb = new(_local!, new SessionLyrics(this), karaoke: Lyrics, ownerRole: _role.ToString().ToLowerInvariant(),
                lan: settings, localIpc: false);
            await _lanWeb.StartAsync(token);
            _lanHostError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.Net.Sockets.SocketException or System.Security.Cryptography.CryptographicException)
        {
            if (_lanWeb != null) { await _lanWeb.DisposeAsync(); _lanWeb = null; }
            if (_lanHostError == null) Console.Error.WriteLine(Localization.Get("LanSharingUnavailable"));
            _lanHostError = "LanSharingUnavailable";
        }
    }
    private volatile string? _selectedLanId;
    private volatile LanFrame? _lanFrame;
    private volatile LanClient? _lanClient;
    private Task? _lanRun;
    private sealed record LanFrame(LyricsSnapshot Snapshot, long Revision);
    public bool CanControl => _selectedLanId == null || _lanFrame != null && _lanClient?.Peer.AllowControl == true;
    public string? RemoteDeviceName => _selectedLanId == null ? null : _lanClient?.Peer.Name;
    private bool HasLanSelection => _selectedLanId != null;

    private async Task RunLanAsync(CancellationToken token)
    {
        var store = new LanTrustStore();
        long discoverAt = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var settings = UserConfiguration.LoadLan();
                    var selected = settings.SelectedDeviceId;
                    if (_selectedLanId != selected)
                    {
                        _lanFrame = null;
                        _selectedLanId = selected;
                        _lanClient?.Dispose(); _lanClient = null;
                        discoverAt = 0;
                        EmitStateChanged(null!);
                    }
                    if (selected != null)
                    {
                        var peer = store.Peers().FirstOrDefault(p => p.Id == selected);
                        if (peer == null) { _lanFrame = null; _lanClient?.Dispose(); _lanClient = null; }
                        else
                        {
                            if (_lanClient == null || _lanClient.Peer.Token != peer.Token)
                            { _lanClient?.Dispose(); _lanClient = new LanClient(peer); }
                            var snapshot = await _lanClient.ReadAsync(token);
                            if (snapshot != null)
                            {
                                var revision = snapshot.LyricsVersion == _lanFrame?.Snapshot.LyricsVersion
                                    ? _lanFrame.Revision : Interlocked.Increment(ref _remoteRevision);
                                _lanFrame = new(snapshot, revision);
                                EmitStateChanged(snapshot.State!);
                            }
                            else
                            {
                                _lanFrame = null; EmitStateChanged(null!);
                                if (Environment.TickCount64 >= discoverAt)
                                {
                                    discoverAt = Environment.TickCount64 + 15000;
                                    var found = (await LanDiscovery.FindAsync(settings.DiscoveryPort, token)).FirstOrDefault(d => d.Id == selected);
                                    if (found != null)
                                    {
                                        // Discovery can change a route, never the certificate pin or credentials.
                                        _lanClient.Dispose();
                                        _lanClient = new LanClient(peer with { Host = found.Host, Port = found.Port });
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e) when (e is IOException or ArgumentException or System.Text.Json.JsonException or System.Net.Sockets.SocketException)
                { _lanFrame = null; }
                await Task.Delay(_lanFrame?.Snapshot.State?.Playing == true ? 100 : 300, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { _lanFrame = null; _lanClient?.Dispose(); _lanClient = null; }
    }
}
