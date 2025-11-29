using OmniLyrics.Backends.CiderV3;
using OmniLyrics.Backends.Windows;
using OmniLyrics.Core;

namespace OmniLyrics.Backends.Factory;

public static class BackendFactory
{
    public static IPlayerBackend Create()
    {
        if (CiderV3Api.IsAvailableAsync().Result)
            return new CiderV3Backend();

        if (OperatingSystem.IsWindows())
            return new SMTCBackend();

        throw new PlatformNotSupportedException();
    }
}