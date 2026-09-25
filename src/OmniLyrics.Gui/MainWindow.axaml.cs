using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using OmniLyrics.Gui.Models;
using OmniLyrics.Core;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using System.Linq;
using OmniLyrics.Gui.Utils;

namespace OmniLyrics.Gui;

public partial class MainWindow : Window
{
    private bool _hovered;
    private string? _appliedPreset;
    private Size? _pendingPresetSize;
    private WindowEdge? _resizeEdge;
    private readonly WindowsBackdrop _windowsBackdrop = new();
    private readonly HyprlandBackdrop _hyprlandBackdrop = new();
    public bool IsLocked => AppearancePreferences.Current.Locked;
    public MainWindow() : this(new LyricsViewModel()) { }
    public MainWindow(LyricsViewModel viewModel)
    {
        InitializeComponent();
        foreach (var button in this.GetLogicalDescendants().OfType<Button>())
            button.PropertyChanged += (_, e) =>
            {
                // Let a captured press complete before hiding its toolbar. This also
                // handles capture cancellation without leaving the toolbar pinned.
                if (e.Property == Button.IsPressedProperty)
                    Avalonia.Threading.Dispatcher.UIThread.Post(UpdateOverlay);
            };
        // Hyprland otherwise restores the overlay into a tile. Other window
        // managers may disallow fullscreen for utility windows, so keep their
        // ordinary window type.
        if (System.OperatingSystem.IsLinux() && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            X11Properties.SetNetWmWindowType(this, Avalonia.Controls.Platform.X11NetWmWindowType.Utility);
        DataContext = viewModel;
        AppearancePreferences.Changed += ApplyAppearance;
        Localization.Changed += RefreshLockAction;
        ApplyAppearance();
        RootBorder.Measuring = ApplyResponsiveLayout;
        Closed += (_, _) => AppearancePreferences.Changed -= ApplyAppearance;
        Closed += (_, _) => Localization.Changed -= RefreshLockAction;
        Closed += (_, _) => _hyprlandBackdrop.Dispose();
        Opened += (_, _) => ApplyBackdrop(force: true);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty)
                _windowsBackdrop.Apply(this, AppearancePreferences.Current.UseBlur, AppearancePreferences.Current.ThemeMode == "dark");
        };
        Closing += (s, e) =>
        {
            if (e.CloseReason is WindowCloseReason.WindowClosing or WindowCloseReason.Undefined)
            {
                Hide();
                e.Cancel = true;
            }
        };
    }

    private void RootBorder_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _hovered = true;
        UpdateOverlay();
    }

    private void RootBorder_OnPointerExited(object? sender, PointerEventArgs e)
    {
        _hovered = false;
        UpdateOverlay();
    }

    private void ApplyAppearance()
    {
        var settings = AppearancePreferences.Current;
        var reading = settings.Preset is "focus" or "portrait" or "fullscreen";
        var portrait = settings.Preset == "portrait";
        var fullscreen = settings.Preset == "fullscreen";
        var resize = _appliedPreset != settings.Preset;
        // Clear the preceding reading layout's constraints before shrinking
        // into a floating preset. Apply both native dimensions together below.
        MinWidth = 380;
        MinHeight = reading ? 440 : (settings.Preset == "compact" ? 50 + settings.FontSize * 1.5 : 80 + settings.FontSize * 2.3)
            + (settings.ShowTranslation ? settings.TranslationFontSize * 1.5 : 0);
        CanResize = true;
        PrimaryLyric.FontSize = ReadingLyric.FontSize = settings.FontSize;
        PrimaryLyric.Height = settings.FontSize * 1.5;
        PrimaryLyric.HighlightBrush = ReadingLyric.HighlightBrush = Brush.Parse(settings.HighlightColor);
        PrimaryLyric.BaseBrush = ReadingLyric.BaseBrush = Brush.Parse(settings.TextColor);
        ClassicTranslation.FontSize = ReadingTranslation.FontSize = settings.TranslationFontSize;
        ClassicTranslation.Foreground = ReadingTranslation.Foreground = Brush.Parse(settings.TextColor);
        foreach (var line in new[] { EarlierTranslation, PreviousTranslation, NextTranslation, LaterTranslation })
        { line.Foreground = Brush.Parse(settings.TextColor); line.FontSize = System.Math.Max(12, settings.TranslationFontSize * .85); }
        foreach (var line in new[] { EarlierContext, PreviousContext, NextContext, LaterContext, SecondLyric })
        { line.Foreground = Brush.Parse(settings.TextColor); line.FontSize = settings.FontSize * .78; }
        LyricsPanel.IsVisible = !reading;
        ReadingLayout.IsVisible = reading;
        ReadingLyric.AlignLeft = false;
        ReadingTranslation.TextAlignment = TextAlignment.Center;
        foreach (var line in new[] { EarlierContext, PreviousContext, NextContext, LaterContext,
            EarlierTranslation, PreviousTranslation, NextTranslation, LaterTranslation })
            line.TextAlignment = TextAlignment.Center;
        ReadingTranslation.LineHeight = settings.TranslationFontSize * 1.5;
        foreach (var line in new[] { EarlierTranslation, PreviousTranslation, NextTranslation, LaterTranslation })
            line.LineHeight = line.FontSize * 1.5;
        foreach (var line in new[] { EarlierContext, PreviousContext, NextContext, LaterContext })
            line.LineHeight = line.FontSize * 1.4;
        SecondLyric.IsVisible = settings.Preset != "compact";
        LyricsPanel.Margin = new Thickness(0, 26, 0, 0);
        RootBorder.CornerRadius = new CornerRadius(reading ? 24 : 12);
        AppLogo.IsVisible = settings.ShowLogo;
        MetadataLogo.IsVisible = settings.ShowLogo;
        PlayerInformation.IsVisible = settings.ShowPlayerInfo;
        MetadataPlayer.IsVisible = settings.ShowPlayerInfo;
        HeaderText.IsVisible = !portrait;
        HeaderText.MaxWidth = settings.Preset == "compact" ? 180 : 260;
        PlayerInformation.MaxWidth = portrait ? 70 : 120;
        ToolbarIdentity.IsVisible = !reading;
        OverlayPlayback.IsVisible = !reading;
        QuickPlayback.IsVisible = reading;
        if (resize)
        {
            _appliedPreset = settings.Preset;
            var size = settings.Preset switch
            {
                "compact" => new Size(760, 50 + settings.FontSize * 1.5 + (settings.ShowTranslation ? settings.TranslationFontSize * 1.5 : 0)),
                "focus" => new Size(1040, 580),
                "portrait" => new Size(460, 740),
                "fullscreen" => new Size(1200, 800),
                _ => new Size(860, 80 + settings.FontSize * 2.3 + (settings.ShowTranslation ? settings.TranslationFontSize * 1.5 : 0))
            };
            var leavingFullscreen = IsVisible && WindowState == WindowState.FullScreen && !fullscreen;
            _pendingPresetSize = leavingFullscreen ? size : null;
            // Full-screen restoration is asynchronous: the window manager's
            // restored geometry would overwrite a resize sent before it finishes.
            // While entering fullscreen, let the window manager choose the size:
            // a following normal-size request can cancel fullscreen on X11.
            if (!leavingFullscreen && !fullscreen) SetPresetSize(size, true);
            WindowState = fullscreen ? WindowState.FullScreen : WindowState.Normal;
        }
        ApplyResponsiveLayout(new Size(Bounds.Width > 0 ? Bounds.Width : Width, Bounds.Height > 0 ? Bounds.Height : Height));
        ApplyBackdrop();
        UpdateOverlay();
        UpdateLockAction();
    }

    private void SetPresetSize(Size size, bool resizeClient)
    {
        Width = size.Width;
        Height = size.Height;
        if (IsVisible && resizeClient) ClientSize = size;
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        if (_pendingPresetSize is not { } size || WindowState != WindowState.Normal) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_pendingPresetSize != size || WindowState != WindowState.Normal) return;
            _pendingPresetSize = null;
            SetPresetSize(size, true);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void ApplyResponsiveLayout(Size size)
    {
        var settings = AppearancePreferences.Current;
        if (settings.Preset is not ("focus" or "portrait" or "fullscreen") || size.Width <= 0 || size.Height <= 0) return;
        var fullscreen = settings.Preset == "fullscreen";
        var vertical = settings.Preset == "portrait" || (!fullscreen && size.Width < 760);
        var inset = System.Math.Clamp(size.Width * .04, 20, 80);
        ReadingLayout.Margin = new Thickness(inset, 58, inset, 28);
        var scale = vertical ? System.Math.Clamp(System.Math.Min(size.Width / 460, size.Height / 740), .85, 1.4)
            : System.Math.Clamp(System.Math.Min(size.Width / 1040, size.Height / 580), .9, 1.65);
        ReadingLyric.FontSize = settings.FontSize * scale;
        ReadingLyric.MaxHeight = System.Math.Max(120, (size.Height - (vertical ? 320 : 120)) * .6);
        ReadingTranslation.FontSize = settings.TranslationFontSize * scale;
        ReadingTranslation.LineHeight = ReadingTranslation.FontSize * 1.5;
        foreach (var line in new[] { EarlierContext, PreviousContext, NextContext, LaterContext })
        { line.FontSize = settings.FontSize * scale * .78; line.LineHeight = line.FontSize * 1.4; }
        foreach (var line in new[] { EarlierTranslation, PreviousTranslation, NextTranslation, LaterTranslation })
        { line.FontSize = System.Math.Max(12, settings.TranslationFontSize * scale * .85); line.LineHeight = line.FontSize * 1.5; }

        var sideWidth = System.Math.Clamp((size.Width - 2 * inset) * .32, 220, 460);
        var gap = System.Math.Clamp(size.Width * .055, 36, 100);
        ReadingLayout.ColumnDefinitions = vertical || fullscreen ? new ColumnDefinitions("*")
            : new ColumnDefinitions { new(sideWidth, GridUnitType.Pixel), new(gap, GridUnitType.Pixel), new(1, GridUnitType.Star) };
        ReadingLayout.RowDefinitions = new RowDefinitions(vertical ? "Auto,*" : fullscreen ? "*,Auto" : "*");
        Grid.SetColumn(ContextPanel, vertical || fullscreen ? 0 : 2); Grid.SetRow(ContextPanel, vertical ? 1 : 0);
        Grid.SetRow(SongPanel, fullscreen ? 1 : 0);
        ContextPanel.MaxWidth = fullscreen ? System.Math.Min(size.Width - 2 * inset, 1200) : double.PositiveInfinity;
        ContextPanel.HorizontalAlignment = fullscreen ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Stretch;
        SongPanel.MaxWidth = fullscreen ? 480 : double.PositiveInfinity;
        SongPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        SongPanel.VerticalAlignment = vertical || fullscreen ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Center;
        SongPanel.SideBySide = vertical || fullscreen;
        SongPanel.ArtworkSize = sideWidth;
        SongTitleRow.HorizontalAlignment = vertical || fullscreen ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Center;
        SongArtist.TextAlignment = MetadataPlayer.TextAlignment = vertical || fullscreen ? TextAlignment.Left : TextAlignment.Center;
        QuickPlayback.HorizontalAlignment = vertical || fullscreen ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Center;
    }

    private void UpdateOverlay()
    {
        var show = _hovered || TopBar.GetVisualDescendants().OfType<Button>().Any(button => button.IsPressed);
        // Keep the buttons arranged while hidden. Removing/reinserting them makes
        // the compositor's hit-test scene lag behind fast pointer-entry clicks.
        TopBar.Opacity = show ? 1 : 0;
        TopBar.IsHitTestVisible = show;
        TopBar.IsEnabled = show;
    }

    private void ApplyBackdrop(bool force = false)
    {
        var settings = AppearancePreferences.Current;
        RootBorder.Background = new SolidColorBrush(Color.Parse(settings.BackgroundColor), settings.BackgroundOpacity);
        TransparencyBackgroundFallback = new SolidColorBrush(Color.Parse(settings.BackgroundColor));
        TransparencyLevelHint = settings.UseBlur
            ? [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent]
            : [WindowTransparencyLevel.Transparent];
        _windowsBackdrop.Apply(this, settings.UseBlur, settings.ThemeMode == "dark");
        if (IsVisible) _hyprlandBackdrop.Apply(settings.UseBlur, Title ?? "", force);
    }

    private void LockButton_Click(object? sender, RoutedEventArgs e)
    {
        try { AppearancePreferences.ToggleLock(); }
        catch { /* A failed preference write leaves the window unlocked. */ }
    }

    private void RefreshLockAction() => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateLockAction);

    private void UpdateLockAction()
    {
        LockIcon.Data = (Geometry)Resources[IsLocked ? "lock_regular" : "unlock_regular"]!;
        LockIcon.Foreground = (IBrush)Application.Current!.Resources[IsLocked ? "AccentBrush" : "LyricPrimary"]!;
        Avalonia.Automation.AutomationProperties.SetName(LockAction, Localization.Get(IsLocked ? "UnlockWindow" : "LockWindow"));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            AppearancePreferences.Save(AppearancePreferences.Current with { Preset = "focus" });
            e.Handled = true;
        }
        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            LockButton_Click(this, e);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private async void PrevButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LyricsViewModel vm) return;

        await vm.Backend.PreviousAsync();
    }

    private async void PlayPauseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LyricsViewModel vm) return;

        await vm.Backend.TogglePlayPauseAsync();
    }

    private async void NextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LyricsViewModel vm) return;

        await vm.Backend.NextAsync();
    }

    private async void FavoriteButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LyricsViewModel vm) await vm.ToggleFavoriteAsync();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        // Window close action
        Close();
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (App.Current?.DataContext is TrayViewModel tray)
            tray.OpenSettings();
        else
            await new SettingsWindow().ShowDialog(this);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Handled || IsButton(e.Source)) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && ResizeEdgeAt(e.GetPosition(this)) is { } edge)
        {
            BeginResizeDrag(edge, e);
            e.Handled = true;
            return;
        }

        // Enable window dragging on left-button press
        if (!IsLocked && WindowState != WindowState.FullScreen && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private static bool IsButton(object? source) => source is Visual visual
        && (visual is Button || visual.GetVisualAncestors().Any(parent => parent is Button));

    private WindowEdge? ResizeEdgeAt(Point point)
    {
        if (!CanResize || WindowState == WindowState.FullScreen) return null;
        // Avalonia coordinates are logical pixels; the grab area scales with DPI.
        const double grip = 8;
        var left = point.X < grip;
        var right = point.X >= Bounds.Width - grip;
        var top = point.Y < grip;
        var bottom = point.Y >= Bounds.Height - grip;
        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (_, true, true, _) => WindowEdge.NorthEast,
            (true, _, _, true) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.West,
            (_, true, _, _) => WindowEdge.East,
            (_, _, true, _) => WindowEdge.North,
            (_, _, _, true) => WindowEdge.South,
            _ => null
        };
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var edge = IsButton(e.Source) ? null : ResizeEdgeAt(e.GetPosition(this));
        if (_resizeEdge == edge) return;
        _resizeEdge = edge;
        Cursor = new Cursor(edge switch
        {
            WindowEdge.North or WindowEdge.South => StandardCursorType.SizeNorthSouth,
            WindowEdge.West or WindowEdge.East => StandardCursorType.SizeWestEast,
            WindowEdge.NorthWest or WindowEdge.SouthEast => StandardCursorType.TopLeftCorner,
            WindowEdge.NorthEast or WindowEdge.SouthWest => StandardCursorType.TopRightCorner,
            _ => StandardCursorType.Arrow
        });
    }
}
