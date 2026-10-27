using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace Spotifly.App.Services;

/// <summary>Extracts a vivid, readable accent from album artwork without retaining artwork bytes.</summary>
public static class AlbumArtworkPaletteExtractor
{
    private static readonly Color FallbackAccent = Color.FromRgb(30, 215, 96);

    public static Color ExtractAccent(BitmapSource artwork)
    {
        if (artwork.PixelWidth <= 0 || artwork.PixelHeight <= 0)
            return FallbackAccent;

        var converted = new FormatConvertedBitmap(artwork, PixelFormats.Bgra32, null, 0);
        int step = Math.Max(1, Math.Min(converted.PixelWidth, converted.PixelHeight) / 36);
        double totalWeight = 0;
        double red = 0, green = 0, blue = 0;

        for (int y = 0; y < converted.PixelHeight; y += step)
        {
            for (int x = 0; x < converted.PixelWidth; x += step)
            {
                var pixel = new byte[4];
                converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
                if (pixel[3] < 180)
                    continue;

                double max = Math.Max(pixel[2], Math.Max(pixel[1], pixel[0]));
                double min = Math.Min(pixel[2], Math.Min(pixel[1], pixel[0]));
                double saturation = max <= 0 ? 0 : (max - min) / max;
                if (saturation < 0.30 || max < 45)
                    continue;

                double weight = saturation * (0.35 + (max / 255.0));
                red += pixel[2] * weight;
                green += pixel[1] * weight;
                blue += pixel[0] * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight < 0.01)
            return FallbackAccent;

        var accent = Color.FromRgb(
            (byte)Math.Clamp(Math.Round(red / totalWeight), 0, 255),
            (byte)Math.Clamp(Math.Round(green / totalWeight), 0, 255),
            (byte)Math.Clamp(Math.Round(blue / totalWeight), 0, 255));

        // Keep a subdued cover from making the controls disappear against a dark island.
        double brightest = Math.Max(accent.R, Math.Max(accent.G, accent.B));
        if (brightest < 135)
        {
            double scale = 135 / brightest;
            accent = Color.FromRgb(
                (byte)Math.Min(255, Math.Round(accent.R * scale)),
                (byte)Math.Min(255, Math.Round(accent.G * scale)),
                (byte)Math.Min(255, Math.Round(accent.B * scale)));
        }

        return accent;
    }
}
