using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using OmniLyrics.Gui.Models;

namespace OmniLyrics.Gui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TopBar.IsVisible = false; // Hidden until mouse hover
    }

    private void RootBorder_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        // Show overlay interface with semi-transparent background
        TopBar.IsVisible = true;
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0));
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.AcrylicBlur
        ];
    }

    private void RootBorder_OnPointerExited(object? sender, PointerEventArgs e)
    {
        // Hide overlay and restore full transparency
        TopBar.IsVisible = false;
        RootBorder.Background = Brushes.Transparent;
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.Transparent
        ];
    }

    private async void PrevButton_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (LyricsViewModel)DataContext;
        if (vm == null) return;

        await vm.Backend.PreviousAsync();
    }

    private async void PlayPauseButton_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (LyricsViewModel)DataContext;
        if (vm == null) return;

        await vm.Backend.TogglePlayPauseAsync();
    }

    private async void NextButton_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (LyricsViewModel)DataContext;
        if (vm == null) return;

        await vm.Backend.NextAsync();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        // Window close action
        Close();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Enable window dragging on left-button press
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}