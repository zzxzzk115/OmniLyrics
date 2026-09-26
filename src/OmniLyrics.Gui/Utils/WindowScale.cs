using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using OmniLyrics.Gui.Models;

namespace OmniLyrics.Gui.Utils;

/// <summary>Scale layout, input and popups together, on top of the native monitor DPI.</summary>
internal sealed class WindowScale
{
    private static readonly HashSet<WindowScale> Windows = [];
    private static readonly DispatcherTimer MonitorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private static HyprlandScaleSnapshot? _hyprland;
    private static bool _readingMonitors;
    private readonly Window _window;
    private readonly Action<Size> _resize;
    private readonly LayoutTransformControl _layout;
    private readonly bool _scrollWhenConstrained;
    private Size _minimum;
    public double Factor { get; private set; } = 1;

    public WindowScale(Window window, Action<Size> resize, bool scrollWhenConstrained = false)
    {
        _window = window;
        _resize = resize;
        _scrollWhenConstrained = scrollWhenConstrained;
        _minimum = new Size(window.MinWidth, window.MinHeight);
        var content = (Control)window.Content!;
        window.Content = null;
        _layout = new LayoutTransformControl { Child = content };
        window.Content = scrollWhenConstrained
            ? new ScrollViewer { Content = _layout, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }
            : _layout;
        Windows.Add(this);
        window.ScalingChanged += ScalingChanged;
        AppearancePreferences.Changed += Apply;
        window.Opened += ScalingChanged;
        window.PositionChanged += (_, _) => RefreshMonitors();
        window.SizeChanged += (_, _) => UpdateOverflow();
        window.Closed += (_, _) =>
        {
            Windows.Remove(this);
            if (Windows.Count == 0) MonitorTimer.Stop();
            window.ScalingChanged -= ScalingChanged;
            AppearancePreferences.Changed -= Apply;
        };
        Apply();
        if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            MonitorTimer.Start();
    }

    static WindowScale() => MonitorTimer.Tick += (_, _) => RefreshMonitors();

    private static async void RefreshMonitors()
    {
        if (_readingMonitors || !MonitorTimer.IsEnabled) return;
        _readingMonitors = true;
        try
        {
            var monitors = await HyprlandBackdrop.RunAsync(CancellationToken.None, "-j", "monitors");
            var clients = await HyprlandBackdrop.RunAsync(CancellationToken.None, "-j", "clients");
            var zero = await HyprlandBackdrop.RunAsync(CancellationToken.None, "-j", "getoption", "xwayland:force_zero_scaling");
            if (monitors == null || clients == null || zero == null) return;
            _hyprland = HyprlandScaleSnapshot.Parse(monitors, clients, zero, Environment.ProcessId);
            foreach (var window in Windows) window.Apply();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException
            or KeyNotFoundException or ArgumentException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { /* Retain the last valid monitor data while the compositor is unavailable. */ }
        finally { _readingMonitors = false; }
    }

    private void ScalingChanged(object? sender, EventArgs e) { Apply(); RefreshMonitors(); }

    public Size ToWindowSize(Size size) => new(size.Width * Factor, size.Height * Factor);
    public Size ToContentSize(Size size) => new(size.Width / Factor, size.Height / Factor);

    public void SetMinimum(Size minimum)
    {
        _minimum = minimum;
        ApplyMinimum();
    }

    private Size AvailableSize()
    {
        var screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
        return screen == null ? new Size(double.PositiveInfinity, double.PositiveInfinity)
            : new Size(Math.Max(1, screen.WorkingArea.Width / _window.RenderScaling - 24),
                Math.Max(1, screen.WorkingArea.Height / _window.RenderScaling - 48));
    }

    private void ApplyMinimum()
    {
        var available = AvailableSize();
        _window.MinWidth = Math.Min(_minimum.Width * Factor, available.Width);
        _window.MinHeight = Math.Min(_minimum.Height * Factor, available.Height);
        if (_scrollWhenConstrained)
        {
            _layout.MinWidth = _minimum.Width * Factor;
            _layout.MinHeight = _minimum.Height * Factor;
            UpdateOverflow();
        }
    }

    private void UpdateOverflow()
    {
        if (_window.Content is not ScrollViewer scroll) return;
        var overflow = _window.ClientSize.Width < _minimum.Width * Factor - 1
            || _window.ClientSize.Height < _minimum.Height * Factor - 1;
        scroll.HorizontalScrollBarVisibility = scroll.VerticalScrollBarVisibility = overflow
            ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    public void SetSize(Size contentSize)
    {
        var size = ToWindowSize(contentSize);
        var available = AvailableSize();
        size = new Size(Math.Min(size.Width, available.Width), Math.Min(size.Height, available.Height));
        _window.Width = size.Width;
        _window.Height = size.Height;
        if (_window.IsVisible && _window.WindowState == WindowState.Normal) _resize(size);
    }

    private void Apply()
    {
        var requested = AppearancePreferences.Current.UiScale;
        var next = _hyprland?.Factor(_window.Title ?? "", _window.RenderScaling, requested)
            ?? requested / _window.RenderScaling ?? 1;
        if (Math.Abs(next - Factor) < .00001) { ApplyMinimum(); return; }
        var size = ToContentSize(new Size(_window.Width, _window.Height));
        Factor = next;
        _layout.LayoutTransform = new ScaleTransform(next, next);
        ApplyMinimum();
        if (_window.WindowState == WindowState.Normal) SetSize(size);
    }
}
