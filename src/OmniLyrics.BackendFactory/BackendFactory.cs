using OmniLyrics.Backends.Linux;
using OmniLyrics.Backends.Mac;
using OmniLyrics.Backends.Windows;
using OmniLyrics.Backends.CiderV3;
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

        if (OperatingSystem.IsLinux())
            return new MPRISBackend();

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSMediaControlBackend();
        }

        throw new PlatformNotSupportedException();
    }
}