using Avalonia.Controls;

namespace OmniLyrics.Gui;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        Closing += (s, e) =>
        {
            ((Window)s).Hide();
            e.Cancel = true;
        };
    }
}