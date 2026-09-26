using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using System.Globalization;
using OmniLyrics.Backends.Dynamic;
using OmniLyrics.Core;
using OmniLyrics.Core.Configuration;
using OmniLyrics.Core.Lyrics.Models;
using OmniLyrics.Core.Shared;
using OmniLyrics.Gui;
using OmniLyrics.Gui.Controls;
using OmniLyrics.Gui.Models;
using OmniLyrics.Gui.Utils;
using System.Text.Json.Nodes;

// These tests repeatedly map windows and switch fullscreen. Never run them on
// the user's Linux desktop: use a separate X server (for example Xvfb).
if (OperatingSystem.IsLinux())
{
    var display = Environment.GetEnvironmentVariable("OMNILYRICS_UI_TEST_DISPLAY");
    var server = Environment.GetEnvironmentVariable("OMNILYRICS_UI_TEST_XSERVER_PID");
    var isolated = false;
    if (int.TryParse(server, out var serverPid))
    {
        try
        {
            isolated = File.ReadAllText($"/proc/{serverPid}/comm").Trim() == "Xvfb"
                && File.ReadAllText($"/proc/{serverPid}/cmdline").Split('\0').Contains(display);
        }
        catch (IOException) { }
    }
    if (string.IsNullOrWhiteSpace(display) || display != Environment.GetEnvironmentVariable("DISPLAY") || !isolated)
    {
        Console.Error.WriteLine("UI tests require a verified virtual X server. Run: bash tests/run-ui-smoke.sh");
        Environment.ExitCode = 2;
        return;
    }
}

var config = Path.Combine(Path.GetTempPath(), "omnilyrics-ui-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("OMNILYRICS_CONFIG_DIR", config);
Environment.SetEnvironmentVariable("OMNILYRICS_LANGUAGE", null);
UserConfiguration.SaveLanguage("en"); UserConfiguration.SaveLyrics(new(false, 5));
using (var reserved = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
{
    reserved.Start();
    UserConfiguration.SaveServer(new("127.0.0.1", ((System.Net.IPEndPoint)reserved.LocalEndpoint).Port, 32651, "127.0.0.1"));
}
var passed = 0;
var failures = new List<string>();
void Check(string name, bool condition)
{
    if (condition) { Console.WriteLine("PASS " + name); passed++; }
    else { Console.WriteLine("FAIL " + name); failures.Add(name); }
}
void Pump(int milliseconds = 100)
{
    // Process native resize/focus events as well as queued managed jobs.
    var frame = new DispatcherFrame();
    using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(milliseconds));
    Dispatcher.UIThread.PushFrame(frame);
}
void Until(Func<bool> condition)
{
    for (var i = 0; i < 120 && !condition(); i++) Pump(50);
    if (!condition()) throw new Exception("UI condition timed out");
}
void Resize(Window window, double width, double height)
{
    window.Width = width;
    window.Height = height;
    // The framework exposes its native resize request through a protected setter.
    // Use that setter in tests rather than manually arranging the content tree.
    typeof(TopLevel).GetProperty(nameof(TopLevel.ClientSize))!.SetValue(window, new Size(width, height));
    Pump(300);
}
void Click(SettingsWindow settings, string key) => settings.GetLogicalDescendants().OfType<Button>().Distinct()
    .Single(button => button.Content?.ToString() == Localization.Get(key)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
void PointerClick(Window host, Control target, Point? offset = null, Action? beforeRelease = null)
{
    var point = target.TranslatePoint(offset ?? new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), host)!.Value;
    var hit = host.InputHitTest(point) as Interactive ?? throw new Exception("No input target at " + point);
    if (target is RepeatButton) Console.WriteLine($"Pointer target={target.Name} hit={hit.GetType().Name}:{(hit as Control)?.Name} at {point}");
    using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
    hit.RaiseEvent(new PointerPressedEventArgs(hit, pointer, host, point, 0,
        new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
    beforeRelease?.Invoke();
    (pointer.Captured as Interactive ?? hit).RaiseEvent(new PointerReleasedEventArgs(hit, pointer, host, point, 1,
        new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
    Pump();
}
void Capture(Window window, string name)
{
    if (window is MainWindow main) main.FindControl<Control>("TopBar")!.Opacity = 1;
    var root = (Control)window.Content!;
    var size = window.IsVisible ? window.ClientSize : new Size(window.Width, window.Height);
    if (window.IsVisible) window.UpdateLayout();
    else
    {
        root.Measure(size); root.Arrange(new Rect(size));
        Dispatcher.UIThread.RunJobs();
        root.Measure(size); root.Arrange(new Rect(size));
    }
    var scale = window.RenderScaling;
    using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)), new Vector(96 * scale, 96 * scale));
    bitmap.Render(window is MainWindow ? root : window);
    var folder = args.Length > 0 ? args[0] : "/tmp/omnilyrics-ui-previews";
    Directory.CreateDirectory(folder); bitmap.Save(Path.Combine(folder, name + ".png"));
}
Check("Fresh preferences enable simulated highlighting", UserConfiguration.LoadAppearance().ApproximateHighlight);
Check("Fresh preferences use a 60 percent background", UserConfiguration.LoadAppearance().BackgroundOpacity == .6);
AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
var fake = new DemoBackend();
string[] words = ["Wake with the morning light", "Leave all the noise behind", "We have a little time", "Let every color shine across the wide and open sky", "Follow the music home", "Carry the moment on", "Under an open sky"];
string[] translations = ["随晨光醒来", "让喧嚣留在身后", "此刻时光属于我们", "让每一种色彩闪耀，穿过开阔的天空，让这一刻持续得更久一些", "跟随音乐归家", "让这一刻延续", "在开阔的天空下"];
var lines = words.Select((text, i) => new LyricsLine(TimeSpan.FromSeconds(i * 8), text,
    text.Split(' ').Select((word, n) => new LyricsToken(TimeSpan.FromSeconds(i * 8 + n * 7d / text.Split(' ').Length), TimeSpan.FromSeconds(7d / text.Split(' ').Length), word + (n == text.Split(' ').Length - 1 ? "" : " "))).ToList(),
    new() { ["zh"] = translations[i] }, TimeSpan.FromSeconds(i * 8 + 7))).ToList();
var manager = new LyricsManager((_, _) => Task.FromResult<List<LyricsLine>?>(lines));
manager.UpdateAsync(fake.State, true).GetAwaiter().GetResult();
fake.Available = false;
using var vm = new LyricsViewModel(new DesktopSession(() => fake, () => new Uri("http://127.0.0.1:1/")), manager);
var window = new MainWindow(vm, () => false);
Check("No player starts with a localized message", vm.CurrentLine.Text == "No song is playing");
fake.Available = true;
Check("Floating lyric buttons have no tooltips and keep accessible names",
    window.GetLogicalDescendants().OfType<Button>().All(button => ToolTip.GetTip(button) == null
        && !string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(button))));
Check("All lyric controls disable tooltip services", window.GetVisualDescendants().OfType<Control>().All(control => !ToolTip.GetServiceEnabled(control)));
Until(() => vm.CurrentLine.Text == words[3]);
Check("Bilingual lyric follows the current original line", vm.Translation == translations[3] && vm.PreviousLine.Text == words[2]);
var completeMetadata = fake.State.DeepCopy();
fake.State = completeMetadata.DeepCopy();
fake.State.Title = "Spotify without duration";
fake.State.SourceApp = "org.mpris.MediaPlayer2.spotify";
fake.State.Duration = TimeSpan.Zero;
Until(() => vm.Title == fake.State.Title);
Check("GUI displays Spotify with an unknown duration", vm.CurrentLine.Text == words[3] && vm.Duration == TimeSpan.Zero);
fake.State = fake.State.DeepCopy(); fake.State.Album = null; fake.State.Title = "Incomplete music metadata";
Until(() => vm.Title == fake.State.Title);
Check("Unknown media classification does not become no song playing", MediaTypeDetector.Guess(fake.State) == MediaType.Unknown
    && vm.CurrentLine.Text == words[3]);
fake.State = completeMetadata;
Until(() => vm.Title == completeMetadata.Title);
Check("Background favorite lookup never sets busy or writes", !vm.FavoriteBusy && fake.Writes == 0);
fake.ReadGate.TrySetResult(true);
Until(() => vm.FavoriteAvailable);
var change = vm.ToggleFavoriteAsync();
Check("Only a click sets favorite busy; no optimistic icon change", vm.FavoriteBusy && !vm.IsFavorite && fake.Writes == 1);
fake.WriteGate.TrySetResult(true);
Until(() => change.IsCompleted);
Check("Favorite icon changes after confirmed state", vm.IsFavorite && !vm.FavoriteBusy);
fake.WriteGate = new();
var stale = vm.ToggleFavoriteAsync();
fake.State = fake.State.DeepCopy(); fake.State.Title = "Another track";
Until(() => vm.Title == "Another track"); fake.WriteGate.TrySetResult(true); Until(() => stale.IsCompleted);
Check("A late favorite response cannot paint another track", !vm.IsFavorite && !vm.FavoriteBusy);
fake.SupportsFavorites = false; fake.State.Title = "Unsupported player";
Until(() => vm.Title == "Unsupported player"); Pump(250);
Check("Unsupported connection hides the favorite action", !vm.FavoriteAvailable);
fake.State.Title = "Morning Light"; fake.SupportsFavorites = true;
Until(() => vm.Title == "Morning Light");
fake.Available = false;
Until(() => vm.CurrentLine.Text == "No song is playing");
Check("Disconnect clears the previous song, translation and favorite", vm.Title == null && vm.Translation == null
    && !vm.FavoriteAvailable && vm.SecondaryLine.Text == "" && vm.Progress == 0);
UserConfiguration.SaveLanguage("zh-CN"); Localization.Reload();
Until(() => vm.CurrentLine.Text == "没有正在播放的歌曲");
foreach (var preset in new[] { "classic", "compact", "focus", "portrait", "fullscreen" })
{
    AppearancePreferences.Save(new(preset, false, true, true)); Pump();
    var name = preset is "classic" or "compact" ? "PrimaryLyric" : "ReadingLyric";
    Check(preset + " shows the localized idle message", window.FindControl<KaraokeLine>(name)!.Line?.Text == "没有正在播放的歌曲");
}
UserConfiguration.SaveLanguage("en"); Localization.Reload();
Until(() => vm.CurrentLine.Text == "No song is playing");
fake.Available = true;
Until(() => vm.CurrentLine.Text == words[3]);
Check("Paused music keeps its lyrics", !vm.Playing && vm.CurrentLine.Text == words[3]);
foreach (var preset in new[] { "classic", "compact", "focus", "portrait", "fullscreen" })
{
    AppearancePreferences.Save(new(preset, false, true, true)); Pump();
    Capture(window, "preset-" + preset);
    if (preset is "focus" or "portrait" or "fullscreen")
    {
        var area = window.FindControl<Control>("ContextPanel")!;
        var translation = window.FindControl<TextBlock>("ReadingTranslation")!;
        var next = window.FindControl<TextBlock>("NextContext")!;
        var bottom = translation.TranslatePoint(new Point(0, translation.Bounds.Height), area)!.Value.Y;
        var top = next.TranslatePoint(default, area)!.Value.Y;
        Check(preset + " long lyrics and translation leave space before the next line", top >= bottom + 20);
        Check(preset + " keeps playback by the song and only utility actions in the header",
            window.FindControl<Control>("QuickPlayback")!.IsVisible && !window.FindControl<Control>("OverlayPlayback")!.IsVisible
            && window.FindControl<Control>("TimingBadge") == null);
    }
    Check(preset + " layout renders bilingual text", window.FindControl<TextBlock>(preset is "classic" or "compact" ? "ClassicTranslation" : "ReadingTranslation")!.Text == translations[3]);
}
AppearancePreferences.Save(new("focus", true, false, false, false, false, 40, 22, "#CCDDEE", "#AAEECC", "#102030", .6, false)); Pump();
Check("Lock and identity visibility apply immediately", window.IsLocked && !window.FindControl<Control>("AppLogo")!.IsVisible && !window.FindControl<Control>("PlayerInformation")!.IsVisible);
Check("Position lock keeps resizing and playback available", window.CanResize && window.FindControl<Control>("QuickPlayback")!.IsVisible);
using (var hover = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true))
{
    var root = window.FindControl<Control>("RootBorder")!;
    root.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, root, hover, window, new Point(50, 50), 0, PointerPointProperties.None, KeyModifiers.None));
}
Check("Position lock leaves the toolbar and unlock action available", window.FindControl<Control>("TopBar")!.IsHitTestVisible
    && Avalonia.Automation.AutomationProperties.GetName(window.FindControl<Button>("LockAction")!) == Localization.Get("UnlockWindow"));
Until(() => !vm.HasTranslation);
Check("Typography, colors and translation option apply", window.FindControl<KaraokeLine>("ReadingLyric")!.FontSize == 40 && !vm.HasTranslation
    && ((ISolidColorBrush)window.FindControl<KaraokeLine>("ReadingLyric")!.HighlightBrush).Color == Color.Parse("#AAEECC"));
Check("Blur can be disabled independently of background opacity", window.TransparencyLevelHint.SequenceEqual(new[] { WindowTransparencyLevel.Transparent }));
AppearancePreferences.ToggleLock(); Check("Tray-compatible toggle unlocks the window", !window.IsLocked);
AppearancePreferences.Save(AppearancePreferences.Current with { UseBlur = true });
UserConfiguration.SaveCider("token", "ui-demo-token");
var settings = new SettingsWindow();
settings.Show(); Pump(250);
var navigation = settings.FindControl<TabControl>("SettingsTabs")!;
foreach (var name in new[] { "LyricsTab", "ThemeTab", "GeneralTab", "AboutTab", "AppearanceTab", "PlayerConnectionsTab", "LanTab" })
{
    var tab = settings.FindControl<TabItem>(name)!;
    foreach (var x in new[] { 5d, 24d, 100d, tab.Bounds.Width - 5 })
    {
        navigation.SelectedItem = navigation.Items.OfType<TabItem>().First(t => t != tab); Pump();
        PointerClick(settings, tab, new Point(x, tab.Bounds.Height / 2));
        Check($"Navigation {tab.Name} selects and focuses the clicked row at x={x}", navigation.SelectedItem == tab && tab.IsKeyboardFocusWithin);
    }
}
navigation.SelectedItem = settings.FindControl<TabItem>("LanTab"); Pump();
Check("LAN sharing is opt-in in the real settings UI", settings.FindControl<CheckBox>("LanEnabled")!.IsChecked == false);
Check("LAN playback control permission defaults off", settings.FindControl<CheckBox>("LanControlGrant")!.IsChecked != true);
Check("Private invitations are hidden until explicitly generated", !settings.FindControl<TextBox>("LanInvitation")!.IsVisible);
Check("LAN page title follows the navigation resource", settings.FindControl<TextBlock>("PageTitle")!.Text == Localization.Get("LanDevices"));
Capture(settings, "settings-lan");
var search = settings.FindControl<TextBox>("SettingsSearch")!;
PointerClick(settings, search);
Check("Search accepts pointer focus", search.IsKeyboardFocusWithin);
navigation.Focus(); PointerClick(settings, settings.FindControl<Control>("SearchIcon")!);
Check("Clicking the search icon focuses the search field", search.IsKeyboardFocusWithin);
var searchBorder = search.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
Capture(settings, "search-focused");
Check("Search focus uses the field surface and a single full-width border",
    ((ISolidColorBrush)searchBorder.Background!).Color == ((ISolidColorBrush)Application.Current.Resources["FieldBackground"]!).Color
    && searchBorder.Bounds.Width >= search.Bounds.Width - 2
    && ((ISolidColorBrush)search.Foreground!).Color == ((ISolidColorBrush)Application.Current.Resources["PrimaryText"]!).Color);
Capture(settings, "search-focused");
PointerClick(settings, settings.FindControl<TextBlock>("PageTitle")!);
Check("Clicking non-interactive space releases search focus", !search.IsKeyboardFocusWithin);
PointerClick(settings, search);
search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Pump();
Check("Escape releases search focus without clearing the query", !search.IsKeyboardFocusWithin && string.IsNullOrEmpty(search.Text));
Check("Settings and built-in controls disable tooltip services", settings.GetVisualDescendants().OfType<Control>().All(control => !ToolTip.GetServiceEnabled(control)));
var blurToggle = settings.FindControl<ToggleSwitch>("BlurMode")!;
var opacitySlider = settings.FindControl<Slider>("OpacitySlider")!;
var blurHint = settings.FindControl<TextBlock>("BlurOpacityHint")!;
var lyricBackground = window.FindControl<Border>("RootBorder")!;
Check("Existing blur preferences use a clear background on every platform",
    blurToggle.IsChecked == true && opacitySlider.Value == 0 && !opacitySlider.IsEnabled && blurHint.IsVisible
    && ((ISolidColorBrush)lyricBackground.Background!).Opacity == 0);
blurToggle.IsChecked = false;
Check("Turning off blur restores the saved opacity", opacitySlider.IsEnabled && opacitySlider.Value == 60 && !blurHint.IsVisible);
opacitySlider.Value = 37;
Click(settings, "ApplySettings"); Pump();
Check("Unblurred background uses the chosen opacity", !UserConfiguration.LoadAppearance().UseBlur
    && Math.Abs(((ISolidColorBrush)lyricBackground.Background!).Opacity - .37) < .00001);
blurToggle.IsChecked = true;
Check("Enabling blur locks the background opacity at zero", opacitySlider.Value == 0 && !opacitySlider.IsEnabled && blurHint.IsVisible);
Click(settings, "ApplySettings"); Pump();
Check("Applying blur saves zero opacity and clears the lyric tint", UserConfiguration.LoadAppearance().BackgroundOpacity == 0
    && ((ISolidColorBrush)lyricBackground.Background!).Opacity == 0);
blurToggle.IsChecked = false;
Check("Disabling blur after Apply restores the previous choice", opacitySlider.IsEnabled && opacitySlider.Value == 37);
blurToggle.IsChecked = true;
AppearancePreferences.Save(AppearancePreferences.Current with { BackgroundOpacity = .82 });
Check("An older config with blur and nonzero opacity cannot cover the system backdrop",
    ((ISolidColorBrush)lyricBackground.Background!).Opacity == 0);
settings.FindControl<NumericUpDown>("LyricFontSize")!.Value = 46;
settings.FindControl<ColorPicker>("HighlightColorPicker")!.Color = Color.Parse("#CCAAFF");
settings.FindControl<ToggleSwitch>("BlurMode")!.IsChecked = true;
Click(settings, "ApplySettings"); Pump();
Check("Settings save immediately updates the lyric window", window.FindControl<KaraokeLine>("ReadingLyric")!.FontSize == 46 && UserConfiguration.LoadAppearance().HighlightColor == "#CCAAFF" && UserConfiguration.LoadAppearance().UseBlur);
settings.FindControl<TextBox>("SettingsSearch")!.Text = "字体"; Pump();
Check("Chinese search navigates to appearance even in English", ((TabItem)settings.FindControl<TabControl>("SettingsTabs")!.SelectedItem!).Name == "AppearanceTab");
settings.FindControl<TextBox>("SettingsSearch")!.Text = "网易云扫码"; Pump(300);
Check("Chinese QR search locates the YesPlayMusic connection in English",
    navigation.SelectedItem == settings.FindControl<TabItem>("PlayerConnectionsTab")
    && settings.FindControl<Button>("YesPlayMusicSignInButton")!.Content?.ToString() == "NetEase QR sign-in");
var qrLogin = settings.FindControl<Button>("YesPlayMusicSignInButton")!;
var qrPoint = qrLogin.TranslatePoint(new Point(qrLogin.Bounds.Width / 2, qrLogin.Bounds.Height / 2), settings)!.Value;
Check("Search brings the NetEase sign-in button into the visible page", settings.InputHitTest(qrPoint) is Visual qrHit
    && (qrHit == qrLogin || qrHit.GetVisualAncestors().Contains(qrLogin)));
Check("No stale QR or disconnect action is shown before sign-in", !settings.FindControl<Border>("YesPlayMusicQrPanel")!.IsVisible
    && !settings.FindControl<Button>("YesPlayMusicDisconnectButton")!.IsVisible);
var connectionCard = settings.FindControl<Border>("YesPlayMusicCard")!;
var connectionScroll = connectionCard.GetVisualAncestors().OfType<ScrollViewer>().First();
var cardTop = connectionCard.TranslatePoint(default, connectionScroll)!.Value.Y;
Check("QR search reveals the whole connection card including authorization status",
    cardTop >= -1 && cardTop + connectionCard.Bounds.Height <= connectionScroll.Bounds.Height + 1);
Capture(settings, "settings-yesplaymusic-en");
settings.FindControl<TextBox>("SettingsSearch")!.Text = "Lazy_V"; Pump();
Check("Search locates the author on the About page", ((TabItem)settings.FindControl<TabControl>("SettingsTabs")!.SelectedItem!).Name == "AboutTab");
Capture(settings, "settings-about-en");
settings.FindControl<TextBox>("SettingsSearch")!.Text = "no-such-setting-123"; Pump();
Check("Unmatched search shows an empty-result message", settings.FindControl<TextBlock>("SearchEmpty")!.IsVisible);
settings.FindControl<TextBox>("SettingsSearch")!.Text = ""; Pump();
foreach (var name in new[] { "GeneralTab", "ThemeTab", "AppearanceTab", "AboutTab" })
{
    var tab = settings.FindControl<TabItem>(name)!;
    PointerClick(settings, tab);
    Check("Navigation remains clickable after filtering: " + name, navigation.SelectedItem == tab);
}
settings.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 1;
Capture(settings, "settings-appearance-en");
foreach (var name in new[] { "LyricFontSize", "TranslationSize" })
{
    var number = settings.FindControl<NumericUpDown>(name)!;
    number.BringIntoView(); Pump();
    var increase = number.GetVisualDescendants().OfType<RepeatButton>().Single(b => b.Name == "PART_IncreaseButton");
    var decrease = number.GetVisualDescendants().OfType<RepeatButton>().Single(b => b.Name == "PART_DecreaseButton");
    var edge = decrease.TranslatePoint(new Point(decrease.Bounds.Width, decrease.Bounds.Height), number)!.Value;
    Check(name + " spinner fits inside its numeric field", edge.X <= number.Bounds.Width && edge.Y <= number.Bounds.Height);
    var value = number.Value;
    PointerClick(settings, increase); Check(name + $" increment button works ({value} -> {number.Value})", number.Value == value + 1);
    PointerClick(settings, decrease); Check(name + " decrement button works", number.Value == value);
}
Capture(settings, "numeric-fields");
settings.FindControl<ComboBox>("LanguageMode")!.SelectedIndex = 1; Pump();
Check("Saved tokens are never prefilled into the settings field", string.IsNullOrEmpty(settings.FindControl<TextBox>("TokenInput")!.Text));
Check("Language changes update resources and existing status messages immediately", Application.Current.Resources["Settings"]?.ToString() == "设置"
    && settings.FindControl<TextBlock>("AppearanceStatus")!.Text == Localization.Get("SettingsApplied"));
Capture(settings, "settings-appearance-zh");
var originalCulture = CultureInfo.CurrentUICulture;
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
var language = settings.FindControl<ComboBox>("LanguageMode")!;
language.SelectedIndex = 0; Pump();
Check("Follow system immediately updates the displayed English selection",
    Localization.Language == "en" && language.SelectionBoxItem?.ToString() == "Follow system");
language.SelectedIndex = 1; Pump();
CultureInfo.CurrentUICulture = originalCulture;
var appearanceTab = (TabItem)settings.FindControl<TabControl>("SettingsTabs")!.SelectedItem!;
var scroll = appearanceTab.GetLogicalDescendants().OfType<ScrollViewer>().Single();
var verticalBar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Name == "PART_VerticalScrollBar" && b.TemplatedParent == scroll);
var form = (Control)scroll.Content!;
var right = form.TranslatePoint(new Point(form.Bounds.Width, 0), scroll)!.Value.X;
var barLeft = verticalBar.TranslatePoint(default, scroll)!.Value.X;
Check("Scrollbar has its own gutter outside the settings form", !verticalBar.IsVisible || barLeft >= right + 12);
var applyButton = settings.FindControl<Button>("ApplyButton")!;
Check("Apply has a fixed height and centered content", applyButton.Height == 36
    && applyButton.HorizontalContentAlignment == Avalonia.Layout.HorizontalAlignment.Center
    && applyButton.VerticalContentAlignment == Avalonia.Layout.VerticalAlignment.Center);
Resize(settings, 790, 580); Capture(settings, "settings-minimum");
foreach (var name in new[] { "LyricsTab", "AboutTab", "ThemeTab", "GeneralTab", "PlayerConnectionsTab", "AppearanceTab", "LanTab" })
{
    var tab = settings.FindControl<TabItem>(name)!;
    PointerClick(settings, tab);
    Check("Minimum-size navigation selects the clicked row: " + name, navigation.SelectedItem == tab);
}
Console.WriteLine($"Footer geometry: button={applyButton.TranslatePoint(new Point(applyButton.Bounds.Width, applyButton.Bounds.Height), settings)} client={settings.ClientSize} bounds={settings.Bounds} requested={settings.Width}x{settings.Height}, scale={settings.RenderScaling}");
Check("Minimum settings window keeps footer actions inside the window",
    applyButton.TranslatePoint(new Point(applyButton.Bounds.Width, applyButton.Bounds.Height), settings) is { } buttonEdge
    && buttonEdge.X <= settings.ClientSize.Width + 1 && buttonEdge.Y <= settings.ClientSize.Height + 1);
Resize(settings, 980, 760);
settings.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
Capture(settings, "settings-connections-zh");
settings.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 0;
Capture(settings, "settings-general-zh");
navigation.SelectedItem = settings.FindControl<TabItem>("LyricsTab"); Pump();
Capture(settings, "settings-lyrics-zh");
settings.FindControl<ComboBox>("SearchStrategyMode")!.SelectedIndex = 1;
settings.FindControl<ComboBox>("MatchMode")!.SelectedIndex = 1;
settings.FindControl<ComboBox>("PreferredLyricSourceMode")!.SelectedIndex = 1;
settings.FindControl<ToggleSwitch>("PlayerLyricsEnabled")!.IsChecked = false;
settings.FindControl<ToggleSwitch>("QQLyricsEnabled")!.IsChecked = false;
Click(settings, "ApplySettings"); Pump();
Check("GUI saves the actual search policy and source selection", UserConfiguration.LoadLyrics() is { SearchStrategy: "quick", MatchMode: "strict", PreferredSource: "netease", PlayerSource: false, QQMusicSource: false, NeteaseSource: true });
settings.FindControl<ToggleSwitch>("NeteaseLyricsEnabled")!.IsChecked = false;
Click(settings, "ApplySettings"); Pump();
Check("GUI rejects disabling every source without overwriting preferences", UserConfiguration.LoadLyrics().NeteaseSource
    && settings.FindControl<TextBlock>("AppearanceStatus")!.Text == Localization.Get("LyricSourceRequired"));
settings.FindControl<ToggleSwitch>("NeteaseLyricsEnabled")!.IsChecked = true;
var reloaded = new SettingsWindow();
Check("Reopened settings restore source and match selections", reloaded.FindControl<ComboBox>("MatchMode")!.SelectedIndex == 1
    && reloaded.FindControl<ComboBox>("SearchStrategyMode")!.SelectedIndex == 1 && reloaded.FindControl<ToggleSwitch>("QQLyricsEnabled")!.IsChecked == false);
Check("A new settings window restores saved typography", reloaded.FindControl<NumericUpDown>("LyricFontSize")!.Value == 46 && reloaded.FindControl<ToggleSwitch>("BlurMode")!.IsChecked == true);
Check("Settings has a logo, search icon and fixed Apply action without a config path",
    settings.FindControl<Control>("SettingsLogo") != null && settings.FindControl<Control>("SearchIcon") != null
    && settings.FindControl<Button>("ApplyButton") != null && settings.FindControl<Control>("LocationText") == null);
Check("About uses the project's build version", ApplicationInfo.Version == typeof(App).Assembly.GetName().Version!.ToString(3)
    && settings.FindControl<TextBlock>("VersionText")!.Text!.Contains(ApplicationInfo.Version));

var external = JsonNode.Parse(UserConfiguration.ReadConfigurationText())!;
external["appearance"]!["fontSize"] = 38;
external["appearance"]!["showLogo"] = false;
external["lyrics"]!["prefetchCount"] = 7;
external["lyrics"]!["matchMode"] = "balanced";
external["lyrics"]!["qqMusicSource"] = true;
external["language"] = "en";
File.WriteAllText(UserConfiguration.SettingsPath, external.ToJsonString());
Until(() => settings.FindControl<NumericUpDown>("LyricFontSize")!.Value == 38);
Check("File watcher syncs appearance, cache preferences and language in an open window",
    settings.FindControl<NumericUpDown>("PrefetchCount")!.Value == 7 && !AppearancePreferences.Current.ShowLogo
    && Localization.Language == "en" && window.FindControl<KaraokeLine>("ReadingLyric")!.FontSize == 38);
Check("File watcher synchronizes matching preferences and English labels", settings.FindControl<ComboBox>("MatchMode")!.SelectedIndex == 0
    && settings.FindControl<ToggleSwitch>("QQLyricsEnabled")!.IsChecked == true && settings.FindControl<ComboBox>("SearchStrategyMode")!.SelectionBoxItem?.ToString() == "Basic search only");
Capture(settings, "settings-lyrics-en");
File.WriteAllText(UserConfiguration.SettingsPath, "{\"appearance\":");
Until(() => settings.FindControl<TextBlock>("AppearanceStatus")!.Text == Localization.Get("ConfigurationInvalid"));
Check("An incomplete external edit keeps the last valid settings",
    AppearancePreferences.Current.FontSize == 38 && settings.FindControl<NumericUpDown>("LyricFontSize")!.Value == 38
    && settings.FindControl<TextBlock>("AppearanceStatus")!.Text == Localization.Get("ConfigurationInvalid"));
external["appearance"]!["fontSize"] = 40;
var replacement = UserConfiguration.SettingsPath + ".replacement";
File.WriteAllText(replacement, external.ToJsonString()); File.Move(replacement, UserConfiguration.SettingsPath, true);
Until(() => settings.FindControl<NumericUpDown>("LyricFontSize")!.Value == 40);
Check("Atomic editor replacement syncs settings", AppearancePreferences.Current.FontSize == 40);

var editor = new ConfigurationEditorWindow(); editor.Show(); Pump();
var editorText = editor.FindControl<TextBox>("ConfigurationText")!;
var original = UserConfiguration.ReadConfigurationText(); editorText.Text = "{} broken";
editor.GetLogicalDescendants().OfType<Button>().Single(b => b.Content?.ToString() == Localization.Get("ApplySettings")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check("Configuration editor rejects malformed JSON without touching the file", UserConfiguration.ReadConfigurationText() == original);
var draft = external.DeepClone(); draft["appearance"]!["fontSize"] = 39;
editorText.Text = draft.ToJsonString();
external["lyrics"]!["prefetchCount"] = 9; File.WriteAllText(UserConfiguration.SettingsPath, external.ToJsonString());
Pump(600);
editor.GetLogicalDescendants().OfType<Button>().Single(b => b.Content?.ToString() == Localization.Get("ApplySettings")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check("Configuration editor protects concurrent external changes", UserConfiguration.LoadLyrics().PrefetchCount == 9
    && editor.FindControl<TextBlock>("EditorStatus")!.Text == Localization.Get("ConfigurationConflict"));
editor.GetLogicalDescendants().OfType<Button>().Single(b => b.Content?.ToString() == Localization.Get("ReloadConfiguration")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
var edited = JsonNode.Parse(editorText.Text!)!; edited["appearance"]!["showLogo"] = true;
editorText.Text = edited.ToJsonString();
editor.GetLogicalDescendants().OfType<Button>().Single(b => b.Content?.ToString() == Localization.Get("ApplySettings")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Until(() => settings.FindControl<ToggleSwitch>("ShowLogoMode")!.IsChecked == true);
Check("Advanced editor saves valid JSON and updates the open settings page", AppearancePreferences.Current.ShowLogo);
Capture(editor, "settings-config-editor"); editor.Close();

settings.FindControl<TabControl>("SettingsTabs")!.SelectedItem = settings.FindControl<TabItem>("ThemeTab");
var themeList = settings.FindControl<ComboBox>("ThemePresetMode")!;
void SelectTheme(string key) => themeList.SelectedItem = themeList.Items.Cast<object>().Single(item => item.ToString() == Localization.Get(key));
settings.FindControl<ToggleSwitch>("TranslationMode")!.IsChecked = true;
settings.FindControl<ToggleSwitch>("ShowPlayerMode")!.IsChecked = true;
var originalAppearance = UserConfiguration.LoadAppearance();
SelectTheme("DarkRed"); Click(settings, "ApplySettings"); Pump();
Check("Dark + Red applies the app-wide accent and dark chrome", Application.Current.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark
    && ((ISolidColorBrush)Application.Current.Resources["AccentBrush"]!).Color == Color.Parse("#FA586A"));
var selectedTab = (TabItem)navigation.SelectedItem!;
var navLabel = ((Control)selectedTab.Header!).GetVisualDescendants().OfType<TextBlock>().Single();
var navIcon = ((Control)selectedTab.Header!).GetVisualDescendants().OfType<PathIcon>().Single();
Check("Dark + Red selected navigation has bright text and icon",
    ((ISolidColorBrush)navLabel.Foreground!).Color == Colors.White && ((ISolidColorBrush)navIcon.Foreground!).Color == Colors.White);
var selectedFill = ((ISolidColorBrush)selectedTab.Background!).Color;
static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
var contrast = 1.05 / (.2126 * Linear(selectedFill.R) + .7152 * Linear(selectedFill.G) + .0722 * Linear(selectedFill.B) + .05);
Check("Selected navigation text has at least 4.5:1 contrast", contrast >= 4.5);
Capture(settings, "theme-dark-red"); Capture(window, "lyrics-dark-red");
SelectTheme("LightBlue");
Check("Selecting a palette is a draft until Apply", UserConfiguration.LoadAppearance().ThemeMode == "dark");
Click(settings, "ApplySettings"); Pump();
var light = UserConfiguration.LoadAppearance();
Check("Light + Blue reaches settings and lyrics without changing layout or fonts",
    Application.Current.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Light
    && ((ISolidColorBrush)settings.Background!).Color == Color.Parse("#F5F5F7")
    && ((ISolidColorBrush)window.FindControl<KaraokeLine>("ReadingLyric")!.HighlightBrush).Color == Color.Parse("#0067C8")
    && light.Preset == originalAppearance.Preset && light.FontSize == originalAppearance.FontSize);
Capture(settings, "theme-light-blue"); Capture(window, "lyrics-light-blue");
var wrappedTranslation = window.FindControl<TextBlock>("ReadingTranslation")!;
Check("Long translated lines remain centered after switching palettes",
    wrappedTranslation.TextLayout.TextLines.Count > 1
    && wrappedTranslation.TextLayout.TextLines.Last().Start > 100);


var lightEditor = new ConfigurationEditorWindow(); lightEditor.Show(); Pump();
Check("Advanced editor shares the selected light theme", lightEditor.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light
    && ((ISolidColorBrush)lightEditor.Background!).Color == Color.Parse("#F5F5F7"));
Capture(lightEditor, "config-editor-light"); lightEditor.Close();
settings.FindControl<ColorPicker>("AccentColorPicker")!.Color = Color.Parse("#6851C4");
settings.FindControl<ColorPicker>("HighlightColorPicker")!.Color = Color.Parse("#6851C4");
settings.FindControl<TextBox>("ThemePresetName")!.Text = "Evening Violet";
settings.FindControl<Button>("SaveThemeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(400);
Check("Saving a custom palette keeps unsaved choices but does not activate them",
    UserConfiguration.LoadThemePresets().Single().Name == "Evening Violet"
    && UserConfiguration.LoadAppearance() == light
    && settings.FindControl<ColorPicker>("AccentColorPicker")!.Color == Color.Parse("#6851C4"));
Click(settings, "ApplySettings"); Pump();
var themeReloaded = new SettingsWindow();
Check("Named presets and custom accent return after reopening settings",
    themeReloaded.FindControl<ComboBox>("ThemePresetMode")!.SelectedItem!.ToString() == "Evening Violet"
    && UserConfiguration.LoadAppearance().AccentColor == "#6851C4");
themeReloaded.Close();
settings.FindControl<Button>("DeleteThemeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check("Deleting a preset leaves the active colors intact", UserConfiguration.LoadThemePresets().Count == 0
    && UserConfiguration.LoadAppearance().AccentColor == "#6851C4");
UserConfiguration.SaveAppearance(ThemePalette.BuiltIn["DarkBlue"].Apply(UserConfiguration.LoadAppearance()));
Until(() => settings.FindControl<ColorPicker>("AccentColorPicker")!.Color == Color.Parse("#4C9AFF"));
Check("External palette changes update the open settings and app theme", Application.Current.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark);

AppearancePreferences.Save(new("focus", false, true, true)); window.Show(); Pump(300);
Resize(window, 1600, 900);
Capture(window, "responsive-wide");
Check("Wide reading layout grows artwork and lyric type with the window", window.FindControl<Border>("CoverFrame")!.Bounds.Width > 350
    && window.FindControl<KaraokeLine>("ReadingLyric")!.FontSize > 40);
Resize(window, 580, 900);
Capture(window, "responsive-narrow");
Check("Narrow reading layout stacks metadata above lyrics", Grid.GetRow(window.FindControl<Control>("ContextPanel")!) == 1
    && window.FindControl<Grid>("ReadingLayout")!.ColumnDefinitions.Count == 1
    && window.FindControl<Control>("ContextPanel")!.Bounds.Width > 450);
Resize(window, 1040, 580);
foreach (var (preset, width, height) in new[] { ("compact", 760d, 125d), ("portrait", 460d, 740d),
    ("classic", 860d, 180.6), ("focus", 1040d, 580d), ("fullscreen", 0d, 0d),
    ("focus", 1040d, 580d), ("compact", 760d, 125d) })
{
    AppearancePreferences.Save(new(preset, false, true, true)); Pump(450);
    Console.WriteLine($"Native geometry {preset}: {window.ClientSize}, state={window.WindowState}");
    Check("Live preset switch restores intended window size: " + preset,
        preset == "fullscreen" ? window.WindowState == WindowState.FullScreen
            && window.Screens.ScreenFromWindow(window) is { } screen
            && Math.Abs(window.ClientSize.Width * window.RenderScaling - screen.Bounds.Width) < 3
            && Math.Abs(window.ClientSize.Height * window.RenderScaling - screen.Bounds.Height) < 3
        : window.WindowState == WindowState.Normal && Math.Abs(window.ClientSize.Width - width) < 2 && Math.Abs(window.ClientSize.Height - height) < 2);
}
foreach (var preset in new[] { "compact", "classic" })
{
    AppearancePreferences.Save(new(preset, false, true, true)); Pump();
    Resize(window, 920, 280);
    Console.WriteLine($"Free resize {preset}: client={window.ClientSize}, requested={window.Width}x{window.Height}, resize={window.CanResize}, scale={window.RenderScaling}");
    Check(preset + " supports free resizing above its minimum", window.CanResize && Math.Abs(window.ClientSize.Width - 920) < 2 && Math.Abs(window.ClientSize.Height - 280) < 2);
    AppearancePreferences.Save(AppearancePreferences.Current with { AccentColor = "#6851C4" }); Pump();
    Check(preset + " keeps the user's size when changing colors", Math.Abs(window.ClientSize.Width - 920) < 2 && Math.Abs(window.ClientSize.Height - 280) < 2);
}
fake.State.Title = "亲爱的那不是爱情 · A long song title";
Until(() => vm.Title == fake.State.Title);
foreach (var (preset, width, height) in new[] { ("focus", 1040d, 580d), ("focus", 800d, 440d), ("portrait", 460d, 740d) })
{
    AppearancePreferences.Save(new(preset, false, true, true)); Pump();
    Resize(window, width, height);
    var layout = window.FindControl<Control>("ReadingLayout")!;
    var playback = window.FindControl<Control>("QuickPlayback")!;
    var edge = playback.TranslatePoint(new Point(playback.Bounds.Width, playback.Bounds.Height), layout)!.Value;
    Check(preset + " keeps complete playback controls inside the reading area at " + width + "x" + height,
        edge.X <= layout.Bounds.Width + 1 && edge.Y <= layout.Bounds.Height + 1);
    Capture(window, "complete-" + preset + "-" + width);
}
foreach (var preset in new[] { "classic", "compact", "focus", "portrait", "fullscreen" })
{
    AppearancePreferences.Save(new(preset, false, true, true)); window.Show(); Pump(350);
    var root = window.FindControl<Control>("RootBorder")!;
    using var hover = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
    root.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, root, hover, window, new Point(50, 50), 0, PointerPointProperties.None, KeyModifiers.None));
    Button ActionButton(string key) => window.GetLogicalDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible
        && Avalonia.Automation.AutomationProperties.GetName(b) == Localization.Get(key));
    var play = ActionButton("PlayPause");
    var toggles = fake.Toggles;
    PointerClick(window, play, beforeRelease: () =>
    {
        fake.State.Playing = true;
        Until(() => vm.Playing);
    });
    Check(preset + " playback click survives a state refresh between press and release", fake.Toggles == toggles + 1);
    Check(preset + " playback state changes keep the same button", ReferenceEquals(play, ActionButton("PlayPause")));
    fake.State.Playing = false; Until(() => !vm.Playing);
    var previous = fake.Previous;
    var next = fake.Next;
    PointerClick(window, ActionButton("Previous")); PointerClick(window, ActionButton("Next"));
    Check(preset + " playback buttons dispatch one command per click", fake.Previous == previous + 1 && fake.Next == next + 1);
    var lockButton = window.FindControl<Button>("LockAction")!;
    PointerClick(window, lockButton, beforeRelease: () =>
    {
        root.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, root, hover, window,
            new Point(-1, -1), 0, PointerPointProperties.None, KeyModifiers.None));
        Check(preset + " keeps the toolbar present until a pressed button finishes", window.FindControl<Control>("TopBar")!.IsHitTestVisible);
    });
    Check(preset + " lock click completes despite a hover exit", window.IsLocked);
    Until(() => !window.FindControl<Control>("TopBar")!.IsHitTestVisible);
    Check(preset + " toolbar hides again after release", !window.FindControl<Control>("TopBar")!.IsHitTestVisible);
    root.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, root, hover, window, new Point(50, 50), 0, PointerPointProperties.None, KeyModifiers.None));
    next = fake.Next;
    PointerClick(window, ActionButton("Next"));
    Check(preset + " locking position still permits playback commands", fake.Next == next + 1);
    PointerClick(window, lockButton);
    Check(preset + " unlock button responds to the next click", !window.IsLocked);
}
window.Hide();
// Absolute UI scaling must also work when the native display is already HiDPI.
AppearancePreferences.Save(AppearancePreferences.Current with { Preset = "classic", UiScale = null });
window.Show(); Pump(300);
var scaleMode = settings.FindControl<ComboBox>("UiScaleMode")!;
var customScale = settings.FindControl<NumericUpDown>("CustomUiScale")!;
var settingsTabs = settings.FindControl<TabControl>("SettingsTabs")!;
settingsTabs.SelectedItem = settings.FindControl<TabItem>("GeneralTab");
settings.Show(); Pump();
double PhysicalScale(Control control, TopLevel host) => Math.Abs(control.TransformToVisual(host)!.Value.M11) * host.RenderScaling;
var lyricRoot = window.FindControl<Control>("RootBorder")!;
Check("UI scale defaults to the native monitor scale", UserConfiguration.LoadAppearance().UiScale == null
    && Math.Abs(PhysicalScale(lyricRoot, window) - window.RenderScaling) < .01);
var typeSize = UserConfiguration.LoadAppearance().FontSize;
for (var i = 1; i <= 5; i++)
{
    var expected = 1 + (i - 1) * .25;
    scaleMode.SelectedIndex = i; Pump(350);
    Check($"{expected:P0} scales all windows immediately without double DPI",
        Math.Abs(PhysicalScale(lyricRoot, window) - expected) < .01
        && Math.Abs(PhysicalScale(scaleMode, settings) - expected) < .01
        && UserConfiguration.LoadAppearance().UiScale == expected);
    Check($"{expected:P0} keeps the lyric window's minimum size scaled",
        Math.Abs(window.MinWidth * window.RenderScaling - 380 * expected) < 1);
}
Check("Interface scaling keeps configured lyric typography unchanged", UserConfiguration.LoadAppearance().FontSize == typeSize);
scaleMode.SelectedIndex = 6; customScale.Value = 142; Pump(350);
Check("Custom scaling persists and keeps its numeric editor visible", UserConfiguration.LoadAppearance().UiScale == 1.42
    && scaleMode.SelectedIndex == 6 && settings.FindControl<Control>("CustomScaleRow")!.IsVisible
    && Math.Abs(PhysicalScale(lyricRoot, window) - 1.42) < .01);
var scaleEditor = new ConfigurationEditorWindow(); scaleEditor.Show(); Pump();
Check("New configuration editors inherit the current scale", Math.Abs(PhysicalScale(scaleEditor.FindControl<TextBox>("ConfigurationText")!, scaleEditor) - 1.42) < .01);
scaleEditor.Close();
scaleMode.IsDropDownOpen = true; Pump();
var scalePopup = scaleMode.GetVisualDescendants().OfType<Popup>().FirstOrDefault()
    ?? scaleMode.GetLogicalDescendants().OfType<Popup>().First();
var popupRoot = (TopLevel)scalePopup.Child!.GetVisualRoot()!;
Check("Scale selector popup inherits the interface transform", scalePopup.InheritsTransform
    && Math.Abs(PhysicalScale(scalePopup.Child, popupRoot) - 1.42) < .01);
scaleMode.IsDropDownOpen = false; Pump();
PointerClick(settings, settings.FindControl<TextBox>("SettingsSearch")!);
Check("Search remains clickable after custom scaling", settings.FindControl<TextBox>("SettingsSearch")!.IsFocused);
Capture(settings, "settings-scale-custom");
customScale.Value = 300; Pump(350);
var scaleScroll = (ScrollViewer)settings.Content!;
Check("Large custom scale keeps the window on screen with scrollable controls", settings.ClientSize.Height * settings.RenderScaling <= 1600
    && scaleScroll.Extent.Height > scaleScroll.Viewport.Height);
scaleScroll.Offset = new Vector(0, scaleScroll.Extent.Height); Pump();
var applyInWindow = settings.FindControl<Button>("ApplyButton")!.TranslatePoint(new Point(0, 0), settings)!.Value;
Check("Large-scale settings footer remains reachable by scrolling", applyInWindow.Y >= 0 && applyInWindow.Y < settings.ClientSize.Height);
customScale.Value = 142; Pump();
settings.FindControl<ComboBox>("PresetMode")!.SelectedIndex = 2;
settings.FindControl<ToggleSwitch>("LockedMode")!.IsChecked = true;
scaleMode.SelectedIndex = 1; Pump();
Check("Changing scale preserves other unsaved settings", settings.FindControl<ComboBox>("PresetMode")!.SelectedIndex == 2
    && settings.FindControl<ToggleSwitch>("LockedMode")!.IsChecked == true);
var scaleDocument = JsonNode.Parse(UserConfiguration.ReadConfigurationText())!;
scaleDocument["appearance"]!["uiScale"] = 1.6;
File.WriteAllText(UserConfiguration.SettingsPath, scaleDocument.ToJsonString());
Until(() => UserConfiguration.LoadAppearance().UiScale == AppearancePreferences.Current.UiScale);
Pump(500);
Check("File edits update scale and the settings selector without restart", customScale.Value == 160
    && Math.Abs(PhysicalScale(lyricRoot, window) - 1.6) < .01);
scaleMode.SelectedIndex = 0; Pump(350);
Check("Follow system removes the manual override immediately", UserConfiguration.LoadAppearance().UiScale == null
    && Math.Abs(PhysicalScale(lyricRoot, window) - window.RenderScaling) < .01);
foreach (var invalidScale in new[] { 0, .5, 3.01, double.NaN, double.PositiveInfinity })
{
    var rejected = false;
    try { UserConfiguration.SaveAppearance(AppearancePreferences.Current with { UiScale = invalidScale }); }
    catch (ArgumentException) { rejected = true; }
    Check($"Invalid UI scale {invalidScale} is rejected", rejected);
}

Console.WriteLine("Verified native render scale: " + window.RenderScaling);
window.Hide();
vm.Dispose(); window.Hide(); settings.Close(); reloaded.Close(); Pump(400); Directory.Delete(config, true);
Console.WriteLine($"{passed} UI checks passed");
if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} UI checks failed: " + string.Join("; ", failures));
    Environment.ExitCode = 1;
}

sealed class DemoBackend : BasePlayerBackend, ITrackFavorites
{
    public PlayerState State = new() { Title = "Morning Light", Artists = ["OmniLyrics Demo"], Album = "Original sample", SourceApp = "Demo", PlayerName = "Demo player", Duration = TimeSpan.FromSeconds(180), Position = TimeSpan.FromSeconds(26.4), Playing = false };
    public TaskCompletionSource<bool> ReadGate = new(), WriteGate = new();
    public int Writes, Toggles, Previous, Next;
    public bool SupportsFavorites = true, Favorite, Available = true;
    public override PlayerState? GetCurrentState() => Available ? State : null;
    public override Task StartAsync(CancellationToken token) => Task.CompletedTask;
    public override Task PlayAsync() => Task.CompletedTask;
    public override Task PauseAsync() => Task.CompletedTask;
    public override Task TogglePlayPauseAsync() { Toggles++; return Task.CompletedTask; }
    public override Task NextAsync() { Next++; return Task.CompletedTask; }
    public override Task PreviousAsync() { Previous++; return Task.CompletedTask; }
    public override Task SeekAsync(TimeSpan position) => Task.CompletedTask;
    public async Task<FavoriteState?> GetFavoriteAsync(PlayerState expected, CancellationToken token)
    {
        await ReadGate.Task;
        return SupportsFavorites ? new("demo:" + expected.Title, LyricsCache.TrackKey(expected), Favorite) : null;
    }
    public async Task<FavoriteState?> SetFavoriteAsync(PlayerState expected, FavoriteState previous, bool favorite, CancellationToken token)
    { Writes++; await WriteGate.Task; Favorite = favorite; return previous with { IsFavorite = favorite }; }
}
