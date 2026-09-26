using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace OmniLyrics.Gui.Utils;

/// <summary>Desktop Acrylic fallback when Avalonia's GPU compositor cannot provide blur.</summary>
internal sealed class WindowsBackdrop
{
    private bool _applied;

    public bool Apply(Window window, bool enabled, bool dark)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)
            || window.TryGetPlatformHandle() is not { Handle: var handle } || handle == IntPtr.Zero)
            return false;

        var nativeBlur = window.ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur
            || window.ActualTransparencyLevel == WindowTransparencyLevel.Blur;
        if (!enabled || nativeBlur)
        {
            if (_applied)
            {
                SetAttribute(handle, 38, 1); // DWMSBT_NONE
                var clear = new Margins();
                DwmExtendFrameIntoClientArea(handle, ref clear);
                _applied = false;
            }
            return nativeBlur && enabled;
        }

        SetAttribute(handle, 20, dark ? 1 : 0); // DWMWA_USE_IMMERSIVE_DARK_MODE
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (DwmExtendFrameIntoClientArea(handle, ref margins) != 0) return false;
        // Documented Windows 11 system backdrop: Desktop Acrylic over the entire client area.
        _applied = SetAttribute(handle, 38, 3); // DWMWA_SYSTEMBACKDROP_TYPE / DWMSBT_TRANSIENTWINDOW
        if (!_applied)
        {
            margins = new Margins();
            DwmExtendFrameIntoClientArea(handle, ref margins);
        }
        return _applied;
    }

    private static bool SetAttribute(IntPtr handle, int attribute, int value)
        => DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
}
