using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Spotifly.App.Models;

namespace Spotifly.App.Services;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    event EventHandler<AppTheme>? ThemeChanged;
    void ApplyTheme(AppTheme theme);
    void ApplyArtworkPalette(Color accent);
}

/// <summary>
/// Manages dynamic application theme switching at runtime by hot-swapping ResourceDictionary instances
/// in Application.Current.Resources without restarting or recreating existing windows.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly ILoggerService? _logger;
    private AppTheme _currentTheme = AppTheme.Minimal;
    private ResourceDictionary? _activeThemeDictionary;

    public AppTheme CurrentTheme => _currentTheme;
    public event EventHandler<AppTheme>? ThemeChanged;

    public ThemeService(ILoggerService? logger = null)
    {
        _logger = logger;
    }

    public void ApplyTheme(AppTheme theme)
    {
        try
        {
            var uri = theme switch
            {
                AppTheme.Modern => new Uri("pack://application:,,,/Spotifly.App;component/Themes/Modern.xaml", UriKind.Absolute),
                AppTheme.Glassmorphism => new Uri("pack://application:,,,/Spotifly.App;component/Themes/Glassmorphism.xaml", UriKind.Absolute),
                AppTheme.AlbumCover => new Uri("pack://application:,,,/Spotifly.App;component/Themes/AlbumCover.xaml", UriKind.Absolute),
                _ => new Uri("pack://application:,,,/Spotifly.App;component/Themes/Minimal.xaml", UriKind.Absolute)
            };

            var newDict = new ResourceDictionary { Source = uri };

            var app = System.Windows.Application.Current;
            if (app != null)
            {
                app.Dispatcher.Invoke(() =>
                {
                    var merged = app.Resources.MergedDictionaries;

                    // Remove existing theme dictionaries (keep Shared.xaml intact)
                    for (int i = merged.Count - 1; i >= 0; i--)
                    {
                        var src = merged[i].Source?.ToString() ?? string.Empty;
                        if (src.Contains("Minimal.xaml") || src.Contains("Modern.xaml") || src.Contains("Glassmorphism.xaml") || src.Contains("AlbumCover.xaml"))
                        {
                            merged.RemoveAt(i);
                        }
                    }

                    merged.Add(newDict);
                    _activeThemeDictionary = newDict;
                    _currentTheme = theme;
                    _logger?.LogInformation($"Theme applied successfully: {theme}");
                    ThemeChanged?.Invoke(this, theme);
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to switch theme to {theme}.", ex);
        }
    }

    /// <summary>Updates only the album-driven brushes while the Album Cover appearance is selected.</summary>
    public void ApplyArtworkPalette(Color accent)
    {
        if (_currentTheme != AppTheme.AlbumCover || System.Windows.Application.Current == null)
            return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var resources = _activeThemeDictionary;
            if (resources == null)
                return;
            var dark = Blend(accent, Color.FromRgb(10, 12, 12), 0.16);
            var panel = Blend(accent, Color.FromRgb(20, 22, 22), 0.10);

            resources["IslandBackgroundBrush"] = Brush(dark, 242);
            resources["IslandBorderBrush"] = Brush(accent, 150);
            resources["IslandAccentBrush"] = Brush(accent);
            resources["IslandButtonHoverBrush"] = Brush(accent, 54);
            resources["IslandButtonPressedBrush"] = Brush(accent, 82);
            resources["IslandCardBackgroundBrush"] = Brush(panel, 235);
            resources["IslandCardBorderBrush"] = Brush(accent, 64);
            resources["InputBorderBrush"] = Brush(accent, 90);
            resources["ProgressArcTrackBrush"] = Brush(accent, 44);
            resources["ProgressArcForegroundBrush"] = Brush(accent);
            resources["ProgressArcGlowBrush"] = Brush(accent, 112);
            _logger?.LogInformation("Album Cover appearance palette updated from current artwork.");
        });
    }

    private static SolidColorBrush Brush(Color color, byte? alpha = null)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha ?? color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Color Blend(Color foreground, Color background, double amount) =>
        Color.FromRgb(
            (byte)Math.Round(background.R + ((foreground.R - background.R) * amount)),
            (byte)Math.Round(background.G + ((foreground.G - background.G) * amount)),
            (byte)Math.Round(background.B + ((foreground.B - background.B) * amount)));
}
