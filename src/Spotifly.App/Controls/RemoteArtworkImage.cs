using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Spotifly.App.Controls;

/// <summary>
/// Loads public Spotify artwork without blocking the WPF layout thread.  WPF's
/// built-in remote URI binding can silently leave an Image blank, so playlist
/// covers use the same download-and-decode pattern as the Now Playing artwork.
/// </summary>
public sealed class RemoteArtworkImage : System.Windows.Controls.Image
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly ConcurrentDictionary<string, BitmapSource> Cache = new(StringComparer.Ordinal);
    private int _loadVersion;

    public static readonly DependencyProperty SourceUrlProperty =
        DependencyProperty.Register(
            nameof(SourceUrl),
            typeof(string),
            typeof(RemoteArtworkImage),
            new PropertyMetadata(null, OnSourceUrlChanged));

    public string? SourceUrl
    {
        get => (string?)GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    private static void OnSourceUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var image = (RemoteArtworkImage)d;
        image.Source = null;
        var version = ++image._loadVersion;
        if (e.NewValue is string url && Uri.TryCreate(url, UriKind.Absolute, out _))
            _ = image.LoadAsync(url, version);
    }

    private async Task LoadAsync(string url, int version)
    {
        try
        {
            if (!Cache.TryGetValue(url, out var bitmap))
            {
                var bytes = await HttpClient.GetByteArrayAsync(url);
                using var stream = new MemoryStream(bytes);
                var decoded = new BitmapImage();
                decoded.BeginInit();
                decoded.CacheOption = BitmapCacheOption.OnLoad;
                decoded.DecodePixelWidth = 120;
                decoded.StreamSource = stream;
                decoded.EndInit();
                decoded.Freeze();
                bitmap = Cache.GetOrAdd(url, decoded);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (version == _loadVersion && SourceUrl == url)
                    Source = bitmap;
            });
        }
        catch (HttpRequestException)
        {
            // The template's placeholder remains visible when Spotify artwork is unavailable.
        }
        catch (TaskCanceledException)
        {
            // A timed-out artwork request is non-fatal and intentionally silent.
        }
        catch (NotSupportedException)
        {
            // Ignore malformed image payloads from a remote endpoint.
        }
    }
}
