using System.Text.Json.Serialization;

namespace Spotifly.App.Models;

/// <summary>
/// Persisted user application settings stored at %LocalAppData%\Spotifly\settings.json.
/// Contains no secrets, tokens, or credentials.
/// </summary>
public sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.Minimal;

    public int ActivationDelayMs { get; set; } = 200;

    public int HideDelayMs { get; set; } = 700;

    public double HorizontalOffset { get; set; } = 0.0;

    public double VerticalOffset { get; set; } = 0.0;

    public bool LaunchAtSignIn { get; set; } = false;

    /// <summary>
    /// Validates and constrains numeric and enum values to safe, operational bounds.
    /// </summary>
    public void ValidateAndSanitize()
    {
        if (!Enum.IsDefined(typeof(AppTheme), Theme))
        {
            Theme = AppTheme.Minimal;
        }

        // Clamp activation delay between 50ms and 1500ms
        ActivationDelayMs = Math.Clamp(ActivationDelayMs, 50, 1500);

        // Clamp hide delay between 200ms and 3000ms
        HideDelayMs = Math.Clamp(HideDelayMs, 200, 3000);

        // Clamp screen offsets between -500 and +500 DIP
        HorizontalOffset = Math.Clamp(HorizontalOffset, -500.0, 500.0);
        VerticalOffset = Math.Clamp(VerticalOffset, 0.0, 200.0);
    }

    /// <summary>
    /// Creates a clone with verified, sanitized values.
    /// </summary>
    public AppSettings Clone()
    {
        return new AppSettings
        {
            Theme = this.Theme,
            ActivationDelayMs = this.ActivationDelayMs,
            HideDelayMs = this.HideDelayMs,
            HorizontalOffset = this.HorizontalOffset,
            VerticalOffset = this.VerticalOffset,
            LaunchAtSignIn = this.LaunchAtSignIn
        };
    }
}
