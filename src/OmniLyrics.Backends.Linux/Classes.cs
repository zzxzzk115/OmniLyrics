namespace OmniLyrics.Backends.Linux;

public class Player
{
    private readonly IPlayer _proxy;

    internal Player(string name, IPlayer proxy)
    {
        Name = name;
        _proxy = proxy;
    }

    public string Name { get; }

    public async Task<PlayerMetadata> GetMetadataAsync()
    {
        var metadataDict = await _proxy.GetAsync<IDictionary<string, object>>("Metadata");
        return PlayerMetadata.FromDictionary(metadataDict);
    }

    public async Task<long> GetPositionAsync() => await _proxy.GetAsync<long>("Position");

    public async Task<string> GetPlaybackStatusAsync() => await _proxy.GetAsync<string>("PlaybackStatus");

    public Task PlayAsync() => _proxy.PlayAsync();
    public Task PauseAsync() => _proxy.PauseAsync();
    public Task PlayPauseAsync() => _proxy.PlayPauseAsync();
    public Task NextAsync() => _proxy.NextAsync();
    public Task PreviousAsync() => _proxy.PreviousAsync();
    public Task SeekAsync(long microseconds) => _proxy.SeekAsync(microseconds);
    public Task SetPositionAsync(string trackId, long position) => _proxy.SetPositionAsync(trackId, position);
}