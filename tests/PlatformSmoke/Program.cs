using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OmniLyrics.Gui;
using OmniLyrics.Gui.Utils;

// Runs on disposable Windows/macOS CI machines; Linux UI tests use Xvfb.
if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) return;
var config = Path.Combine(Path.GetTempPath(), "omnilyrics-platform-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("OMNILYRICS_CONFIG_DIR", config);
AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
void Check(string name, bool value)
{
    if (!value) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
{
    var window = new Window { Width = 400, Height = 250, Background = Brushes.Transparent,
        SystemDecorations = SystemDecorations.None, TransparencyLevelHint = [WindowTransparencyLevel.Transparent] };
    window.Show();
    var frame = new DispatcherFrame();
    using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(200));
    Dispatcher.UIThread.PushFrame(frame);
    var backdrop = new WindowsBackdrop();
    Check("Windows 11 accepts Desktop Acrylic without Avalonia composition blur", backdrop.Apply(window, true, true));
    var handle = window.TryGetPlatformHandle()!.Handle;
    Check("DWM reports the requested Acrylic backdrop", Native.DwmGetWindowAttribute(handle, 38, out var value, 4) == 0 && value == 3);
    backdrop.Apply(window, false, false);
    Check("Disabling blur clears the DWM fallback", Native.DwmGetWindowAttribute(handle, 38, out value, 4) == 0 && value == 1);
    window.Close();
}
if (OperatingSystem.IsMacOS())
{
    var app = Native.Send(Native.objc_getClass("NSApplication"), Native.sel_registerName("sharedApplication"));
    var icon = Native.Send(app, Native.sel_registerName("applicationIconImage"));
    Check("macOS portable app installs the embedded ICNS as its Dock icon", icon != IntPtr.Zero);
    var representations = Native.Send(icon, Native.sel_registerName("representations"));
    var count = Native.Send(representations, Native.sel_registerName("count")).ToInt64();
    Check("The Dock icon contains multiple resolutions", count > 1);
    Console.WriteLine($"Dock icon representations: {count}");
}
if (Directory.Exists(config)) Directory.Delete(config, true);

static class Native
{
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("/usr/lib/libobjc.A.dylib")] internal static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] internal static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] internal static extern IntPtr Send(IntPtr receiver, IntPtr selector);
}
