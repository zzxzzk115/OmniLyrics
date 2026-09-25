using System.Reflection;

namespace OmniLyrics.Gui.Utils;

public static class ApplicationInfo
{
    public static string Version => (typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(App).Assembly.GetName().Version?.ToString(3) ?? "").Split('+')[0];
}
