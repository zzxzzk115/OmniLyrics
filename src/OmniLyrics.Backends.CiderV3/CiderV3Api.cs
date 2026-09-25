using System.Text;
using System.Text.Json;

namespace OmniLyrics.Backends.CiderV3;

/// <summary>
///     Cider v3 RPC API client.
///     https://cider.sh/docs/client/rpc
/// </summary>
public class CiderV3Api : IDisposable
{
    private readonly HttpClient _http;

    private readonly string _playbackApiPrefix = "/api/v1/playback";

    public CiderV3Api(string baseUrl = "http://localhost:10767")
    {
        BaseUrl = baseUrl;
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

    public static async Task<bool> IsAvailableAsync(CancellationToken token = default)
    {
        try
        {
            var api = CreateDefault();
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
            using var resp = await _http.GetAsync(GetPlaybackApiEndpoint("/active"), token);
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
            using var resp = await _http.GetAsync(GetPlaybackApiEndpoint("/is-playing"), token);
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
            using var resp = await _http.GetAsync(GetPlaybackApiEndpoint("/now-playing"), token);
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

            await _http.PostAsync(GetPlaybackApiEndpoint(path), content);
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
}