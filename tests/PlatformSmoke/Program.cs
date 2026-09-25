using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OmniLyrics.Backends.Mac;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using OmniLyrics.Gui;
using OmniLyrics.Gui.Utils;
using OmniLyrics.Gui.Models;
using OmniLyrics.Gui.Controls;
using OmniLyrics.Backends.Dynamic;
using OmniLyrics.Core;
using OmniLyrics.Core.Shared;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Lyrics.Models;

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
    // AppKit can wrap an ICNS container in a single NSImageRep. Check its
    // embedded images with ImageIO rather than counting NSImage wrappers.
    using var stream = AssetLoader.Open(new Uri("avares://OmniLyrics.Gui/Assets/app.icns"));
    using var bytes = new MemoryStream(); stream.CopyTo(bytes);
    var data = Native.CFDataCreate(IntPtr.Zero, bytes.ToArray(), (nint)bytes.Length);
    var source = Native.CGImageSourceCreateWithData(data, IntPtr.Zero);
    try
    {
        Check("ImageIO decodes the embedded ICNS", source != IntPtr.Zero);
        var count = Native.CGImageSourceGetCount(source);
        Check("The embedded Dock icon contains multiple resolutions", count > 1);
        Console.WriteLine($"ICNS images decoded by macOS: {count}");
    }
    finally
    {
        if (source != IntPtr.Zero) Native.CFRelease(source);
        if (data != IntPtr.Zero) Native.CFRelease(data);
    }
}
// Exercise the same layout transform on both native desktop backends.
var settings = new SettingsWindow(); settings.Show();
void Pump()
{
    var frame = new DispatcherFrame();
    using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(250));
    Dispatcher.UIThread.PushFrame(frame);
}
Pump();
var scaleMode = settings.FindControl<ComboBox>("UiScaleMode")!;
foreach (var index in new[] { 1, 3, 5, 0 })
{
    scaleMode.SelectedIndex = index; Pump();
    var target = index == 0 ? settings.RenderScaling : index == 1 ? 1 : index == 3 ? 1.5 : 2;
    var actual = Math.Abs(scaleMode.TransformToVisual(settings)!.Value.M11) * settings.RenderScaling;
    Check($"Native window applies {target:P0} UI scaling without double DPI", Math.Abs(actual - target) < .01);
}
Check("macOS environment tab only appears on macOS", settings.FindControl<TabItem>("MacEnvironmentTab")!.IsVisible == OperatingSystem.IsMacOS());
if (OperatingSystem.IsMacOS())
{
    for (int attempt = 0; attempt < 2; attempt++)
    {
        var checking = MacStartupSetup.CheckAsync(settings, () => settings, default,
            _ => Task.FromResult(new MacEnvironmentStatus(false, false, null, null)));
        Pump();
        var prompt = settings.OwnedWindows.Single();
        var buttons = prompt.GetVisualDescendants().OfType<Button>().ToList();
        Check($"Unavailable GUI startup {attempt + 1} displays installation prompt", !checking.IsCompleted
            && buttons.Any(button => button.Name == "MacStartupInstall" && !button.IsEnabled));
        buttons.Single(button => button.Name == "MacStartupLater").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        Check("Not now completes setup without installing", checking.IsCompletedSuccessfully && settings.OwnedWindows.Count == 0);
    }
    var healthy = MacStartupSetup.CheckAsync(settings, () => settings, default,
        _ => Task.FromResult(new MacEnvironmentStatus(true, false, null, null)));
    Check("Healthy native GUI startup does not prompt", healthy.IsCompletedSuccessfully && settings.OwnedWindows.Count == 0);
}
if (OperatingSystem.IsMacOS())
{
    var opacity = settings.FindControl<Slider>("OpacitySlider")!;
    var blur = settings.FindControl<ToggleSwitch>("BlurMode")!;
    Check("macOS blur presents an automatic zero-opacity background", opacity.Value == 0 && !opacity.IsEnabled);
    blur.IsChecked = false;
    Check("Disabling blur restores the editable background opacity", opacity.IsEnabled && opacity.Value == 60);
    blur.IsChecked = true;
    Check("Re-enabling blur clears its covering background layer", opacity.Value == 0 && !opacity.IsEnabled);
    var original = AppearancePreferences.Current;
    using var model = new LyricsViewModel(new DesktopSession(() => new IdleBackend(), () => new Uri("http://127.0.0.1:1/")), new LyricsManager());
    var lyrics = new MainWindow(model); lyrics.Show(); Pump();
    Console.WriteLine("Actual native transparency: " + lyrics.ActualTransparencyLevel);
    Check("macOS reports real native Blur", lyrics.ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur);
    var effect = Native.FindBlur(lyrics.TryGetPlatformHandle()!.Handle);
    Check("macOS preserves Avalonia's original blur material", effect != IntPtr.Zero
        && (nint)Native.Send(effect, Native.sel_registerName("material")) == 1);
    Check("The native blur view is visible and fills the lyric window", effect != IntPtr.Zero
        && Native.Send(effect, Native.sel_registerName("isHidden")) == IntPtr.Zero
        && Native.Rect(effect, Native.sel_registerName("frame")).Width >= lyrics.ClientSize.Width - 1
        && Native.Rect(effect, Native.sel_registerName("frame")).Height >= lyrics.ClientSize.Height - 1);
    Check("The toolbar leaves the native glass visible between controls", lyrics.FindControl<Grid>("TopBar")!.Background is ISolidColorBrush { Color.A: 0 });
    AppearancePreferences.Save(original with { FontSize = original.FontSize + 1 }); Pump();
    Check("Unrelated appearance changes do not disable native blur", lyrics.ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur);
    Check("The lyric canvas cannot cover native blur with the saved 60 percent tint", ((ISolidColorBrush)lyrics.FindControl<Border>("RootBorder")!.Background!).Opacity == 0);
    AppearancePreferences.Save(original with { UseBlur = false }); Pump();
    Check("Turning blur off restores the user's solid-background tint", ((ISolidColorBrush)lyrics.FindControl<Border>("RootBorder")!.Background!).Opacity == original.BackgroundOpacity);
    foreach (var mode in new[] { "dark", "light" })
    {
        var appearance = original with { UseBlur = true, ThemeMode = mode, TextColor = "#FFFFFF", HighlightColor = "#FFFFFF" };
        AppearancePreferences.Save(appearance); Pump();
        var palette = LyricPalette.Create(appearance);
        Check($"{mode} transparent lyrics contrast with their outline", LyricPalette.Contrast(palette.Text, palette.Outline) >= 4.5
            && LyricPalette.Contrast(palette.Highlight, palette.Outline) >= 4.5);
        bool readable = true;
        foreach (var desktop in new[] { Colors.White, Colors.Black, Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow, Colors.Magenta })
        {
            var surface = palette.ControlSurface; var a = surface.A / 255d;
            var composite = Color.FromRgb((byte)(surface.R * a + desktop.R * (1-a)),
                (byte)(surface.G * a + desktop.G * (1-a)), (byte)(surface.B * a + desktop.B * (1-a)));
            readable &= new[] { palette.Primary, palette.Secondary, palette.Muted, palette.Accent }.All(ink => LyricPalette.Contrast(ink, composite) >= 4.5);
        }
        Check($"{mode} controls keep 4.5:1 contrast over white, black and colored desktops", readable);
        Check($"{mode} karaoke and secondary text keep their local contrast protection", lyrics.FindControl<KaraokeLine>("PrimaryLyric")!.DrawShadow
            && lyrics.FindControl<AlignedTextBlock>("SecondLyric")!.Effect is DropShadowEffect { Opacity: 1 });
    }
    AppearancePreferences.Save(original); lyrics.Hide();
}
settings.Close();
if (Directory.Exists(config)) Directory.Delete(config, true);

static class Native
{
    internal static IntPtr FindBlur(IntPtr window) => FindView(Send(window, sel_registerName("contentView")), 0);
    private static IntPtr FindView(IntPtr view, int depth)
    {
        if (view == IntPtr.Zero || depth > 4) return IntPtr.Zero;
        if (IsKindOfClass(view, sel_registerName("isKindOfClass:"), objc_getClass("NSVisualEffectView")) != 0
            && Send(view, sel_registerName("blendingMode")) == IntPtr.Zero) return view;
        var children = Send(view, sel_registerName("subviews"));
        for (nint i = 0; i < (nint)Send(children, sel_registerName("count")); i++)
        {
            var result = FindView(ObjectAt(children, sel_registerName("objectAtIndex:"), i), depth + 1);
            if (result != IntPtr.Zero) return result;
        }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] internal record struct NativeRect(double X, double Y, double Width, double Height);
    internal static NativeRect Rect(IntPtr receiver, IntPtr selector)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return ArmRect(receiver, selector);
        IntelRect(out var result, receiver, selector);
        return result;
    }
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern NativeRect ArmRect(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend_stret")] private static extern void IntelRect(out NativeRect result, IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr ObjectAt(IntPtr receiver, IntPtr selector, nint index);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern byte IsKindOfClass(IntPtr receiver, IntPtr selector, IntPtr type);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("/usr/lib/libobjc.A.dylib")] internal static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] internal static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] internal static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] internal static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] internal static extern void CFRelease(IntPtr value);
    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")] internal static extern IntPtr CGImageSourceCreateWithData(IntPtr data, IntPtr options);
    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")] internal static extern nuint CGImageSourceGetCount(IntPtr source);
}

sealed class IdleBackend : BasePlayerBackend
{
    public override PlayerState? GetCurrentState() => null;
    public override Task StartAsync(CancellationToken token) => Task.CompletedTask;
    public override Task PlayAsync() => Task.CompletedTask;
    public override Task PauseAsync() => Task.CompletedTask;
    public override Task TogglePlayPauseAsync() => Task.CompletedTask;
    public override Task NextAsync() => Task.CompletedTask;
    public override Task PreviousAsync() => Task.CompletedTask;
    public override Task SeekAsync(TimeSpan position) => Task.CompletedTask;
}
