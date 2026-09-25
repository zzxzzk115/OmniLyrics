using System.Text;
using System.Text.Json;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core;

namespace OmniLyrics.Backends.CiderV3;

/// <summary>
///     Cider V3+ compatibility RPC API client (also available in V4).
///     https://cider.sh/docs/client/rpc
/// </summary>
public class CiderV3Api : IDisposable
{
    private readonly HttpClient _http;
    private readonly string? _explicitAppToken;

    private readonly string _playbackApiPrefix = "/api/v1/playback";

    public CiderV3Api(string baseUrl = "http://127.0.0.1:10767", string? appToken = null)
    {
        BaseUrl = baseUrl;
        _explicitAppToken = appToken;
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMilliseconds(200)
        };
    }

    public string BaseUrl { get; }

    public void Dispose()
    {
        _http.Dispose();
    }

    public static CiderV3Api CreateDefault() => new();

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var configuredToken = _explicitAppToken ?? UserConfiguration.ResolveCiderToken();
        var request = new HttpRequestMessage(method, GetPlaybackApiEndpoint(path));
        if (!string.IsNullOrWhiteSpace(configuredToken)
            && !configuredToken.Contains('\r') && !configuredToken.Contains('\n'))
            request.Headers.Add("apptoken", configuredToken.Trim());
        return request;
    }

    public static async Task<bool> IsAvailableAsync(CancellationToken token = default)
    {
        try
        {
            using var api = CreateDefault();
            return await api.TryGetActiveAsync(token);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TryGetActiveAsync(CancellationToken token = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "/active");
            using var resp = await _http.SendAsync(request, token);
            if (!resp.IsSuccessStatusCode)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TryGetIsPlayingAsync(CancellationToken token = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "/is-playing");
            using var resp = await _http.SendAsync(request, token);
            if (!resp.IsSuccessStatusCode)
                return false;

            string json = await resp.Content.ReadAsStringAsync(token);
            var data = JsonSerializer.Deserialize<CiderIsPlayingResponse>(json);

            return data?.IsPlaying ?? false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<CiderNowPlayingInfo?> TryGetCurrentSongTypedAsync(
        CancellationToken token = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "/now-playing");
            using var resp = await _http.SendAsync(request, token);
            if (!resp.IsSuccessStatusCode)
                return null;

            string json = await resp.Content.ReadAsStringAsync(token);
            var data = JsonSerializer.Deserialize<CiderNowPlayingResponse>(json);

            return data?.Info;
        }
        catch
        {
            return null;
        }
    }

    public async Task PostAsync(string path, object? body = null)
    {
        try
        {
            HttpContent content;

            if (body != null)
            {
                string json = JsonSerializer.Serialize(body);
                content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else
            {
                content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            using var request = CreateRequest(HttpMethod.Post, path);
            request.Content = content;
            using var response = await _http.SendAsync(request);
        }
        catch
        {
            // ignored
        }
    }

    private Task PostSimple(string path)
        => PostAsync(path);

    public Task PlayAsync() => PostSimple("/play");
    public Task PauseAsync() => PostSimple("/pause");
    public Task ToggleAsync() => PostSimple("/playpause");
    public Task NextAsync() => PostSimple("/next");
    public Task PreviousAsync() => PostSimple("/previous");

    public Task SeekAsync(TimeSpan position)
    {
        int sec = (int)position.TotalSeconds;
        return PostAsync("/seek", new
        {
            position = sec
        });
    }

    private string GetPlaybackApiEndpoint(string path) => _playbackApiPrefix + path;

    public async Task<IReadOnlyList<PlayerState>> GetUpcomingTracksAsync(int limit, CancellationToken token = default)
    {
        try
        {
            var current = await TryGetCurrentSongTypedAsync(token);
            if (string.IsNullOrEmpty(current?.PlayParams?.Id)) return [];
            using var request = CreateRequest(HttpMethod.Get, "/queue");
            using var response = await _http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode) return [];
            var items = JsonSerializer.Deserialize<List<CiderQueueItem>>(await response.Content.ReadAsStringAsync(token));
            if (items == null) return [];
            var matches = items.Select((item, index) => (item, index)).Where(x =>
                (x.item.Attributes?.PlayParams?.Id ?? x.item.Id) == current.PlayParams.Id).ToList();
            // The API includes history and exposes no current index. Repeated
            // occurrences of the same ID cannot be ordered reliably.
            if (matches.Count != 1) return [];
            return items.Skip(matches[0].index + 1).Where(item => item.Attributes is { Name.Length: > 0, ArtistName.Length: > 0 })
                .Take(Math.Clamp(limit, 0, 20)).Select(item => new PlayerState
                {
                    Title = item.Attributes!.Name, Artists = [item.Attributes.ArtistName!],
                    Album = item.Attributes.AlbumName, Duration = TimeSpan.FromMilliseconds(item.Attributes.DurationInMillis),
                    SourceApp = "Cider", PlayerName = "Cider"
                }).ToList();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return []; }
    }
}
