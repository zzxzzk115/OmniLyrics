using Tmds.DBus;

namespace OmniLyrics.Backends.Linux;

[DBusInterface("org.mpris.MediaPlayer2.TrackList")]
public interface IPlayerTrackList : IDBusObject
{
    Task<T> GetAsync<T>(string prop);
    Task<IDictionary<string, object>[]> GetTracksMetadataAsync(ObjectPath[] trackIds);
}

[DBusInterface("org.mpris.MediaPlayer2")]
public interface IPlayerApplication : IDBusObject
{
    Task<T> GetAsync<T>(string prop);
}

[DBusInterface("org.mpris.MediaPlayer2.Player")]
public interface IPlayer : IDBusObject
{
    Task PlayAsync();
    Task PauseAsync();
    Task PlayPauseAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task SeekAsync(long microseconds);
    Task SetPositionAsync(ObjectPath trackId, long position);

    Task<T> GetAsync<T>(string prop);
    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
}

[DBusInterface("org.freedesktop.DBus")]
public interface IDBus : IDBusObject
{
    Task<IDisposable> WatchNameOwnerChangedAsync(
        Action<(string name, string oldOwner, string newOwner)> handler);
}
