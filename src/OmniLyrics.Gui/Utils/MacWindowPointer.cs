using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace OmniLyrics.Gui.Utils;

internal static class MacWindowPointer
{
    // AppKit may send LeaveWindow during a native move even though the cursor
    // remains over the borderless lyric window. Query its current base coordinates
    // without relying on the stale pointer event (or requiring input monitoring).
    internal static bool? IsOver(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var handle = window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor != "NSWindow") return null;
        var point = MouseLocation(handle.Handle, Selector("mouseLocationOutsideOfEventStream"));
        return point.X >= 0 && point.X < window.ClientSize.Width
            && point.Y >= 0 && point.Y < window.ClientSize.Height;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public double X, Y; }
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern IntPtr Selector(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern NativePoint MouseLocation(IntPtr receiver, IntPtr selector);
}
