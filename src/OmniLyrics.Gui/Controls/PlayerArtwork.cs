using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace OmniLyrics.Gui.Controls;

/// <summary>Loads player-supplied artwork without blocking rendering or retaining stale covers.</summary>
public sealed class PlayerArtwork : Image
{
    public static readonly StyledProperty<string?> UrlProperty = AvaloniaProperty.Register<PlayerArtwork, string?>(nameof(Url));
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private CancellationTokenSource? _load;
    private Bitmap? _bitmap;
    public string? Url { get => GetValue(UrlProperty); set => SetValue(UrlProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UrlProperty) _ = LoadAsync();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); if (_bitmap == null) _ = LoadAsync(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnDetachedFromVisualTree(e); _load?.Cancel(); Source = null; _bitmap?.Dispose(); _bitmap = null; }

    private async Task LoadAsync()
    {
        _load?.Cancel();
        var cancellation = new CancellationTokenSource(); _load = cancellation;
        Source = null; _bitmap?.Dispose(); _bitmap = null;
        try
        {
            if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri)) return;
            using var content = new MemoryStream();
            const int maximum = 8 * 1024 * 1024;
            if (uri.IsFile)
            {
                using var file = File.OpenRead(uri.LocalPath);
                if (file.Length > maximum) return;
                await file.CopyToAsync(content, cancellation.Token);
            }
            else if (uri.Scheme is "http" or "https")
            {
                using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maximum) return;
                await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
                var buffer = new byte[8192]; int count;
                while ((count = await stream.ReadAsync(buffer, cancellation.Token)) > 0)
                {
                    if (content.Length + count > maximum) return;
                    content.Write(buffer, 0, count);
                }
            }
            else return;
            cancellation.Token.ThrowIfCancellationRequested();
            content.Position = 0;
            var bitmap = await Task.Run(() => Bitmap.DecodeToWidth(content, 512), cancellation.Token);
            if (cancellation.IsCancellationRequested) { bitmap.Dispose(); return; }
            _bitmap = bitmap; Source = bitmap;
        }
        catch { /* The cover placeholder remains available offline. */ }
        finally { if (ReferenceEquals(_load, cancellation)) _load = null; cancellation.Dispose(); }
    }
}
