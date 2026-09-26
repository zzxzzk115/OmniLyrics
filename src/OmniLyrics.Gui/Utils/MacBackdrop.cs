using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace OmniLyrics.Gui.Utils;

internal static class MacBackdrop
{
    internal static void Apply(Window window, bool enabled)
    {
        if (!OperatingSystem.IsMacOS()
            || window.TryGetPlatformHandle() is not { HandleDescriptor: "NSWindow", Handle: var handle }
            || handle == IntPtr.Zero) return;

        var effect = FindBehindWindowEffect(Send(handle, Selector("contentView")), 0);
        if (effect == IntPtr.Zero) return;
        // NSVisualEffectView defaults to FollowsWindowActiveState (0). A lyric
        // overlay needs Active (1) even while another application has focus.
        // Keep Avalonia's material, blending, sizing and visibility unchanged.
        SetState(effect, Selector("setState:"), enabled ? 1 : 0);
    }

    private static IntPtr FindBehindWindowEffect(IntPtr view, int depth)
    {
        if (view == IntPtr.Zero || depth > 8) return IntPtr.Zero;
        if (IsKindOfClass(view, Selector("isKindOfClass:"), objc_getClass("NSVisualEffectView")) != 0
            && Send(view, Selector("blendingMode")) == IntPtr.Zero) return view;
        // Only the behind-window effect belongs to the lyric backdrop. The
        // titlebar's WithinWindow effect must retain AppKit's normal behavior.
        var children = Send(view, Selector("subviews"));
        var count = (nint)Send(children, Selector("count"));
        for (nint index = 0; index < count; index++)
        {
            var effect = FindBehindWindowEffect(ObjectAt(children, Selector("objectAtIndex:"), index), depth + 1);
            if (effect != IntPtr.Zero) return effect;
        }
        return IntPtr.Zero;
    }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC, EntryPoint = "sel_registerName")] private static extern IntPtr Selector(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr ObjectAt(IntPtr receiver, IntPtr selector, nint index);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte IsKindOfClass(IntPtr receiver, IntPtr selector, IntPtr type);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SetState(IntPtr receiver, IntPtr selector, nint state);
}
