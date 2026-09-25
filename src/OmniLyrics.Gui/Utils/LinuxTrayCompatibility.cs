using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace OmniLyrics.Gui.Utils;

/// <summary>
/// Avalonia 11.3 assigns the tooltip text to StatusNotifierItem.Status.
/// Strict hosts (including Quickshell) hide that invalid status. Keep Avalonia's
/// icon/menu implementation and correct only this known invalid value.
/// Upstream: Avalonia/11.3.9/src/Avalonia.FreeDesktop/DBusTrayIconImpl.cs,
/// StatusNotifierItemDbusObj.SetTitleAndTooltip. Remove when upstream is fixed.
/// </summary>
internal sealed class LinuxTrayCompatibility : IDisposable
{
    private readonly DispatcherTimer _timer;

    public LinuxTrayCompatibility(TrayIcon icon)
    {
        // These are private framework internals: fail closed if their shape
        // changes, and never overwrite a valid Active/Passive/NeedsAttention.
        var implementation = typeof(TrayIcon).GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(icon);
        var item = implementation?.GetType().GetField("_statusNotifierItemDbusObj", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(implementation);
        var status = item?.GetType().GetProperty("Status");
        var invalidate = item?.GetType().GetMethod("InvalidateAll");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        if (item == null || status?.CanWrite != true || invalidate == null) return;

        void CorrectStatus()
        {
            if (status.GetValue(item) is not string value
                || value is "Active" or "Passive" or "NeedsAttention") return;
            status.SetValue(item, "Active");
            invalidate.Invoke(item, null);
        }

        _timer.Tick += (_, _) => CorrectStatus();
        // Registration and host reconnection reset the value asynchronously.
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();
}
