using System;
using System.Runtime.InteropServices;

namespace OmniLyrics.Gui.Utils;

/// <summary>
///     https://github.com/AvaloniaUI/Avalonia/discussions/19324
/// </summary>
public static class Win32FocusHelper
{
    private const int SW_RESTORE = 9;

    private const int INPUT_MOUSE = 0;

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static void ForceToForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        // Restore if minimized
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        // Fake mouse input (required by Windows focus model)
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_MOUSE;

        SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));

        // Bring to foreground
        SetForegroundWindow(hwnd);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}