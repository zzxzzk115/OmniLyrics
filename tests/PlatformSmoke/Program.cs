using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
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
Check("Native application identity is OmniLyrics", Application.Current?.Name == "OmniLyrics");
Check("The application menu contains About OmniLyrics", NativeMenu.GetMenu(Application.Current!)?.Items.OfType<NativeMenuItem>().Any(item => item.Header?.ToString() == Localization.Get("AboutOmniLyrics")) == true);
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
void Pump(int milliseconds = 250)
{
    var frame = new DispatcherFrame();
    using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(milliseconds));
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
    var pointerOverWindow = false;
    var lyrics = new MainWindow(model, () => pointerOverWindow); lyrics.Show(); Pump();
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
    AppearancePreferences.Save(original);
    var root = lyrics.FindControl<Control>("RootBorder")!;
    var toolbar = lyrics.FindControl<Control>("TopBar")!;
    using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
    void Hover(bool inside) => root.RaiseEvent(new PointerEventArgs(
        inside ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent,
        root, pointer, lyrics, inside ? new Point(50, 50) : new Point(-1, -1), 0,
        PointerPointProperties.None, KeyModifiers.None));
    var hides = 0;
    toolbar.PropertyChanged += (_, e) => { if (e.Property == Visual.OpacityProperty && toolbar.Opacity == 0) hides++; };
    Hover(true);
    for (var i = 0; i < 5; i++) { Hover(false); Pump(25); Hover(true); Pump(25); }
    // AppKit can deliver the final re-entry noticeably after the mouse release.
    Hover(false); Pump(200); Hover(true); Pump(350);
    Check("Native drag hover churn never hides the toolbar between exits and re-entries", hides == 0 && toolbar.Opacity == 1);
    pointerOverWindow = true;
    Hover(false); Pump(700);
    Check("Stale native hover after dragging keeps controls visible while the cursor is inside", toolbar.Opacity == 1 && toolbar.IsHitTestVisible);
    pointerOverWindow = false; Pump(400);
    Check("Native pointer checks also hide controls when no re-entry event arrives", toolbar.Opacity == 0 && !toolbar.IsHitTestVisible);
    Hover(true);
    Hover(false); Pump(400);
    Check("A sustained pointer exit hides and disables the toolbar", toolbar.Opacity == 0 && !toolbar.IsHitTestVisible && !toolbar.IsEnabled);
    Hover(true);
    Check("Pointer re-entry immediately restores toolbar interaction", toolbar.Opacity == 1 && toolbar.IsHitTestVisible && toolbar.IsEnabled);
    Hover(false); lyrics.Hide();
    Check("Hiding the window cancels pending hover state", toolbar.Opacity == 0 && !toolbar.IsHitTestVisible);
    lyrics.Show(); Pump(50); Hover(true); Pump(400);
    Check("A previous hide request cannot hide a reopened hovered window", toolbar.Opacity == 1 && toolbar.IsHitTestVisible);
    var tray = new TrayViewModel(lyrics, settings, new Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime());
    var app = Application.Current!;
    app.DataContext = tray;
    tray.Scale200Command.Execute(null); Pump();
    Check("Tray scaling applies immediately and marks its current choice", AppearancePreferences.Current.UiScale == 2 && tray.Scale200Text.StartsWith("✓"));
    settings.Position = new PixelPoint(-20000, -20000);
    tray.RecoverInterfaceCommand.Execute(null); Pump();
    var screen = settings.Screens.ScreenFromWindow(settings)!;
    var frameSize = PixelSize.FromSize(settings.FrameSize ?? settings.ClientSize, settings.RenderScaling);
    Check("Tray recovery restores 100 percent without needing the settings controls", AppearancePreferences.Current.UiScale == 1 && settings.IsVisible);
    Check("Tray recovery returns the complete settings window to the monitor", screen.WorkingArea.Contains(settings.Position)
        && settings.Position.X + frameSize.Width <= screen.WorkingArea.Right
        && settings.Position.Y + frameSize.Height <= screen.WorkingArea.Bottom);
    var menu = TrayIcon.GetIcons(app)![0].Menu!;
    var recovery = menu.Items.OfType<NativeMenuItem>().Single(item => item.Command == tray.RecoverInterfaceCommand);
    Check("The native tray exposes recovery at the top level independently of app scale", recovery.Header == Localization.Get("RecoverInterface"));
    tray.CompactCommand.Execute(null); tray.LightCommand.Execute(null); tray.TranslationCommand.Execute(null); Pump();
    Check("Tray common settings update layout, theme and translation", AppearancePreferences.Current.Preset == "compact"
        && AppearancePreferences.Current.ThemeMode == "light" && AppearancePreferences.Current.ShowTranslation != original.ShowTranslation);
    AppearancePreferences.Save(original); app.DataContext = null;
    lyrics.Hide();
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
