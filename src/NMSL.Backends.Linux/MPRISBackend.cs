using NMSL.Core;

namespace NMSL.Backends.Linux;

/// <summary>
/// MPIRS (D-Bus) Backend, only works on Linux
/// </summary>
public class MPRISBackend : IPlayerBackend
{
    private PlayerState? _lastState;

    public event EventHandler<PlayerState>? OnStateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public PlayerState? GetCurrentState() => _lastState;
    
    private static bool StatesEqual(PlayerState? a, PlayerState b)
    {
        if (a is null) return false;

        if (a.Artists.Count != b.Artists.Count)
            return false;

        for (int i = 0; i < a.Artists.Count; i++)
        {
            if (a.Artists[i] != b.Artists[i])
                return false;
        }

        return a.Title == b.Title &&
               a.Position == b.Position &&
               a.Duration == b.Duration &&
               a.Playing == b.Playing &&
               a.SourceApp == b.SourceApp;
    }

    public async Task PlayAsync()
    {
        throw new NotImplementedException();
    }

    public async Task PauseAsync()
    {
        throw new NotImplementedException();
    }

    public async Task TogglePlayPauseAsync()
    {
        throw new NotImplementedException();
    }

    public async Task NextAsync()
    {
        throw new NotImplementedException();
    }

    public async Task PreviousAsync()
    {
        throw new NotImplementedException();
    }

    public async Task SeekAsync(TimeSpan position)
    {
        throw new NotImplementedException();
    }
}
