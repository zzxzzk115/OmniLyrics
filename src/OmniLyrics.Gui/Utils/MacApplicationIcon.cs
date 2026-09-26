using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace OmniLyrics.Gui.Utils;

/// <summary>Gives the portable macOS executable a Dock icon without external resources.</summary>
internal static class MacApplicationIcon
{
    public static void Apply()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var stream = AssetLoader.Open(new Uri("avares://OmniLyrics.Gui/Assets/app.icns"));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var data = SendData(Send(objc_getClass("NSData"), sel_registerName("alloc")),
            sel_registerName("initWithBytes:length:"), bytes, (nuint)bytes.Length);
        if (data == IntPtr.Zero) return;
        IntPtr icon = IntPtr.Zero;
        try
        {
            icon = SendObject(Send(objc_getClass("NSImage"), sel_registerName("alloc")),
                sel_registerName("initWithData:"), data);
            if (icon == IntPtr.Zero) return;
            var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            // NSApplication retains the image, including its Retina representations.
            SendVoid(app, sel_registerName("setApplicationIconImage:"), icon);
        }
        finally
        {
            if (icon != IntPtr.Zero) Release(icon, sel_registerName("release"));
            Release(data, sel_registerName("release"));
        }
    }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendObject(IntPtr receiver, IntPtr selector, IntPtr value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendData(IntPtr receiver, IntPtr selector, byte[] bytes, nuint length);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoid(IntPtr receiver, IntPtr selector, IntPtr value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void Release(IntPtr receiver, IntPtr selector);
}
