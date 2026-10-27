using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spotifly.App.Services;
using Color = System.Windows.Media.Color;

namespace Spotifly.Tests;

public sealed class AlbumArtworkPaletteExtractorTests
{
    [Fact]
    public void ExtractAccent_UsesVividAlbumColour()
    {
        // BGRA: a warm red album-art pixel.
        var artwork = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 20, 30, 180, 255 }, 4);

        Color accent = AlbumArtworkPaletteExtractor.ExtractAccent(artwork);

        Assert.True(accent.R > accent.G * 3);
        Assert.True(accent.R > accent.B * 3);
        Assert.True(accent.R >= 135);
    }

    [Fact]
    public void ExtractAccent_FallsBackWhenArtworkHasNoVividColour()
    {
        var artwork = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 90, 90, 90, 255 }, 4);

        Color accent = AlbumArtworkPaletteExtractor.ExtractAccent(artwork);

        Assert.Equal(Color.FromRgb(30, 215, 96), accent);
    }
}
