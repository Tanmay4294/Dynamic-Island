using System.Windows;
using Spotifly.App.Models;

namespace Spotifly.App.Services;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    event EventHandler<AppTheme>? ThemeChanged;
    void ApplyTheme(AppTheme theme);
}

/// <summary>
/// Manages dynamic application theme switching at runtime by hot-swapping ResourceDictionary instances
/// in Application.Current.Resources without restarting or recreating existing windows.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly ILoggerService? _logger;
    private AppTheme _currentTheme = AppTheme.Minimal;

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
                        if (src.Contains("Minimal.xaml") || src.Contains("Modern.xaml") || src.Contains("Glassmorphism.xaml"))
                        {
                            merged.RemoveAt(i);
                        }
                    }

                    merged.Add(newDict);
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
}
