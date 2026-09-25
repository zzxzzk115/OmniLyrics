using OmniLyrics.Core;

namespace OmniLyrics.Backends.Dynamic;

/// <summary>GUI frontend of the same shared session used by CLI and TUI.</summary>
public sealed class DesktopSession : SharedPlayerSession
{
    public DesktopSession(Func<IPlayerBackend>? createLocal = null, Func<Uri>? serviceAddress = null)
        : base(createLocal ?? (() => new DynamicBackend()), ServiceRole.Gui,
            serviceAddress: serviceAddress, hostService: serviceAddress == null) { }
}
