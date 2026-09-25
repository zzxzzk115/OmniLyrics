using System.Net.Http.Json;
using System.Text.Json;

namespace OmniLyrics.Core;

/// <summary>OmniLyrics' own protocol; never sends Cider credentials.</summary>
public sealed class LyricsServiceClient : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMilliseconds(800) };
    private LyricsSnapshot? _previous;
    private readonly HttpClient _favorites = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(10) };
    private Uri? _previousAddress;

    public async Task<LyricsSnapshot?> TryReadAsync(Uri address, CancellationToken token)
    {
        try
        {
            var snapshot = await _http.GetFromJsonAsync<LyricsSnapshot>(new Uri(address, "snapshot"), token);
            // An occupied port alone does not identify an OmniLyrics instance.
            if (snapshot?.Service != LyricsSnapshot.ServiceName || snapshot.ProtocolVersion != LyricsSnapshot.CurrentProtocol
                || string.IsNullOrEmpty(snapshot.LyricsVersion))
                return null;
            if (address == _previousAddress && snapshot.LyricsVersion == _previous?.LyricsVersion)
                snapshot = snapshot with { Lyrics = _previous.Lyrics };
            _previous = snapshot;
            _previousAddress = address;
            return snapshot;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException or ObjectDisposedException)
        {
            return null;
        }
    }

    public async Task ControlAsync(Uri address, string action, TimeSpan? position, CancellationToken token)
    {
        using var response = position.HasValue
            ? await _http.PostAsJsonAsync(new Uri(address, "playback/seek"), new { position = position.Value.TotalSeconds }, token)
            : await _http.PostAsync(new Uri(address, "playback/" + action), null, token);
        response.EnsureSuccessStatusCode();
    }

    public async Task<FavoriteState?> GetFavoriteAsync(Uri address, CancellationToken token)
    {
        try { return await _favorites.GetFromJsonAsync<FavoriteState>(new Uri(address, "favorites"), token); }
        catch { return null; }
    }

    public async Task<FavoriteState?> SetFavoriteAsync(Uri address, FavoriteState previous, bool favorite, CancellationToken token)
    {
        try
        {
            using var response = await _favorites.PostAsJsonAsync(new Uri(address, "favorites"), new FavoriteRequest(previous, favorite), token);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<FavoriteState>(token) : null;
        }
        catch { return null; }
    }

    public void Dispose() { _http.Dispose(); _favorites.Dispose(); }
}
