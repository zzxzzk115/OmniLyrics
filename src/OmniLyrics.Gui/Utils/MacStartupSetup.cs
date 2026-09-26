using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OmniLyrics.Backends.Mac;
using OmniLyrics.Core;

namespace OmniLyrics.Gui.Utils;

internal static class MacStartupSetup
{
    internal static async Task CheckAsync(Window owner, Func<SettingsWindow> settings, CancellationToken token,
        Func<CancellationToken, Task<MacEnvironmentStatus>>? probe = null)
    {
        try
        {
            var status = await (probe ?? MacEnvironment.ProbeAsync)(token);
            if (!status.NeedsInstallation || token.IsCancellationRequested || !owner.IsVisible) return;
            var prompt = new Window
            {
                Title = Localization.Get("MacEnvironment"), Width = 580, SizeToContent = SizeToContent.Height,
                CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = true
            };
            var later = new Button { Name = "MacStartupLater", Content = Localization.Get("MacNotNow"), Padding = new Thickness(16, 8) };
            var install = new Button { Name = "MacStartupInstall", Content = Localization.Get("MacInstallMediaControl"), Padding = new Thickness(16, 8), IsEnabled = status.BrewPath != null };
            var environment = new Button { Content = Localization.Get("MacOpenEnvironment"), Padding = new Thickness(16, 8) };
            later.Click += (_, _) => prompt.Close(0);
            environment.Click += (_, _) => prompt.Close(1);
            install.Click += (_, _) => prompt.Close(2);
            prompt.Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 20,
                Children =
                {
                    new TextBlock { Text = Localization.Get("MacNoConnection"), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new TextBlock { Text = Localization.Get(status.BrewPath == null ? "MacBrewMissing" : "MacInstallQuestion"), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new WrapPanel { Orientation = Orientation.Horizontal, Children = { later, environment, install } }
                }
            };
            _ = new WindowScale(prompt, size => { prompt.Width = size.Width; prompt.Height = size.Height; });
            using var cancellation = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => prompt.Close(0)));
            var choice = await prompt.ShowDialog<int>(owner);
            if (choice == 0 || token.IsCancellationRequested) return;
            var window = settings(); window.Show(); window.Activate(); window.ShowMacEnvironment();
            if (choice == 2) await window.InstallMacMediaControlAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Console.Error.WriteLine("macOS setup: " + error.Message); }
    }
}
