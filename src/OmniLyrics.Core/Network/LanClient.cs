using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace OmniLyrics.Core.Network;

public sealed class LanClient : IDisposable
{
    private readonly HttpClient _http;
    private string? _version;
    private LyricsSnapshot? _previous;
    public LanPeer Peer { get; }
    public LanClient(LanPeer peer)
    {
        Peer = peer;
        _http = CreatePinned(peer.Host, peer.Port, peer.Fingerprint);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", peer.Token);
    }
    private static HttpClient CreatePinned(string host, int port, string fingerprint)
    {
        LanAddress.Validate(host);
        if (fingerprint.Length != 64 || !fingerprint.All(char.IsAsciiHexDigit)) throw new ArgumentException("Invalid certificate pin.");
        var pin = Convert.FromHexString(fingerprint);
        var handler = new HttpClientHandler
        {
            UseProxy = false, AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate != null
                && DateTime.UtcNow >= certificate.NotBefore.ToUniversalTime() && DateTime.UtcNow <= certificate.NotAfter.ToUniversalTime()
                && CryptographicOperations.FixedTimeEquals(pin, certificate.GetCertHash(HashAlgorithmName.SHA256))
        };
        return new HttpClient(handler) { BaseAddress = new UriBuilder("https", host, port).Uri, Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
    }
    public static async Task<LanPeer> PairAsync(string invitation, string localName, LanTrustStore store, CancellationToken token = default)
    {
        var invite = LanTrustStore.ParseInvitation(invitation);
        if (invite.Id == store.DeviceId) throw new InvalidOperationException(Localization.Get("LanSelfPair"));
        using var http = CreatePinned(invite.Host, invite.Port, invite.Fingerprint);
        using var response = await http.PostAsJsonAsync("lan/pair", new PairingRequest(invite.Secret, localName), token);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PairingResult>(token)
            ?? throw new InvalidDataException("Invalid pairing response.");
        if (result.DeviceId != invite.Id || result.Token.Length != 64 || !result.Token.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid device identity.");
        var peer = new LanPeer(result.DeviceId, invite.Name, invite.Host, invite.Port, invite.Fingerprint, result.Token, result.AllowControl);
        store.SavePeer(peer);
        return peer;
    }
    public async Task<LyricsSnapshot?> ReadAsync(CancellationToken token)
    {
        try
        {
            // Buffer with a strict size limit before JSON parsing untrusted remote data.
            using var response = await _http.GetAsync("snapshot", token);
            response.EnsureSuccessStatusCode();
            var value = await response.Content.ReadFromJsonAsync<LyricsSnapshot>(token);
            if (value?.Service != LyricsSnapshot.ServiceName || value.ProtocolVersion != LyricsSnapshot.CurrentProtocol
                || string.IsNullOrEmpty(value.LyricsVersion)) return null;
            if (value.State is { } state)
            {
                if (state.Artists == null || state.Artists.Count > 128 || state.Artists.Any(a => a == null || a.Length > 4096)
                    || state.Title?.Length > 4096 || state.Position < TimeSpan.Zero || state.Duration < TimeSpan.Zero) return null;
                // A remote file:// URL names a file on a different computer, not a local file to open.
                if (!Uri.TryCreate(state.ArtworkUrl, UriKind.Absolute, out var art) || art.Scheme is not ("https" or "http")) state.ArtworkUrl = null;
            }
            if (value.Lyrics?.Count > 10000 || value.Lyrics?.Any(line => line == null || line.Text == null || line.Text.Length > 16384) == true) return null;
            if (_version == value.LyricsVersion && _previous != null) value = value with { Lyrics = _previous.Lyrics };
            _version = value.LyricsVersion; _previous = value;
            return value;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException) { return null; }
    }
    public async Task ControlAsync(string action, TimeSpan? position, CancellationToken token)
    {
        if (!Peer.AllowControl) throw new InvalidOperationException(Localization.Get("LanReadOnly"));
        using var response = position.HasValue
            ? await _http.PostAsJsonAsync("playback/seek", new { position = position.Value.TotalSeconds }, token)
            : await _http.PostAsync("playback/" + action, null, token);
        response.EnsureSuccessStatusCode();
    }
    public async Task<FavoriteState?> FavoriteAsync(FavoriteRequest? request, CancellationToken token)
    {
        if (!Peer.AllowControl) return null;
        try
        {
            using var response = request == null ? await _http.GetAsync("favorites", token) : await _http.PostAsJsonAsync("favorites", request, token);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<FavoriteState>(token) : null;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException) { return null; }
    }
    public void Dispose() => _http.Dispose();
}
