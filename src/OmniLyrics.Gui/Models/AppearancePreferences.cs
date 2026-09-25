using System;
using Avalonia.Threading;
using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Gui.Models;

public static class AppearancePreferences
{
    public static AppearanceSettings Current { get; private set; } = Read();
    public static event Action? Changed;
    private static readonly DispatcherTimer Timer = new(TimeSpan.FromSeconds(2), DispatcherPriority.Background,
        (_, _) => Refresh());

    static AppearancePreferences() => Timer.Start();

    private static AppearanceSettings Read()
    {
        try { return UserConfiguration.LoadAppearance(); }
        catch { return new("classic", false, true, true); }
    }

    public static void Refresh()
    {
        AppearanceSettings settings;
        try
        {
            UserConfiguration.ValidateConfigurationText(UserConfiguration.ReadConfigurationText());
            settings = UserConfiguration.LoadAppearance();
        }
        catch { return; } // Editors may temporarily leave an incomplete document.
        if (settings == Current) return;
        Current = settings;
        Changed?.Invoke();
    }

    public static void Save(AppearanceSettings settings)
    {
        UserConfiguration.SaveAppearance(settings);
        Current = settings;
        Changed?.Invoke();
    }

    public static void ToggleLock() => Save(Current with { Locked = !Current.Locked });
}
