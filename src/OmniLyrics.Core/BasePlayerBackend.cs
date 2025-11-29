namespace OmniLyrics.Core;

public abstract class BasePlayerBackend : IPlayerBackend
{
    public event EventHandler<PlayerState>? OnStateChanged;

    public virtual void EmitStateChanged(PlayerState state)
    {
        OnStateChanged?.Invoke(this, state);
    }

    public abstract PlayerState? GetCurrentState();
    public abstract Task NextAsync();
    public abstract Task PauseAsync();
    public abstract Task PlayAsync();
    public abstract Task PreviousAsync();
    public abstract Task SeekAsync(TimeSpan position);
    public abstract Task StartAsync(CancellationToken cancellationToken);
    public abstract Task TogglePlayPauseAsync();
}
