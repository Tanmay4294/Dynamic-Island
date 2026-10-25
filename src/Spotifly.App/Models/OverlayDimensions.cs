namespace Spotifly.App.Models;

/// <summary>
/// Central configuration model holding dimensions, activation trigger bounds,
/// and timing delays for the Spotifly Dynamic Island overlay.
/// </summary>
public sealed class OverlayDimensions
{
    /// <summary>
    /// Default compact width in device-independent pixels (DIP).
    /// </summary>
    public double CompactWidth { get; set; } = 250.0;

    /// <summary>
    /// Default compact height in device-independent pixels (DIP).
    /// </summary>
    public double CompactHeight { get; set; } = 55.0;

    /// <summary>
    /// Default expanded width in DIP.
    /// </summary>
    public double ExpandedWidth { get; set; } = 360.0;

    /// <summary>
    /// Default expanded height in DIP.
    /// </summary>
    public double ExpandedHeight { get; set; } = 150.0;

    /// <summary>
    /// Corner radius for compact state.
    /// </summary>
    public double CompactCornerRadius { get; set; } = 26.0;

    /// <summary>
    /// Corner radius for expanded state.
    /// </summary>
    public double ExpandedCornerRadius { get; set; } = 28.0;

    /// <summary>
    /// Backward-compatibility alias for CornerRadius.
    /// </summary>
    public double CornerRadius
    {
        get => CompactCornerRadius;
        set => CompactCornerRadius = value;
    }

    /// <summary>
    /// Horizontal offset in DIP from screen center (for webcam alignment tuning).
    /// </summary>
    public double HorizontalOffset { get; set; } = 0.0;

    /// <summary>
    /// Vertical offset in DIP from the top of the screen work area.
    /// </summary>
    public double VerticalOffset { get; set; } = 0.0;

    /// <summary>
    /// Width of the top activation detection zone in DIP.
    /// </summary>
    public double ActivationZoneWidth { get; set; } = 260.0;

    /// <summary>
    /// Height of the top activation zone extending down from the screen top edge in DIP.
    /// </summary>
    public double ActivationZoneHeight { get; set; } = 6.0;

    /// <summary>
    /// Delay before revealing compact island when entering the activation zone (milliseconds).
    /// </summary>
    public int ActivationDelayMs { get; set; } = 200;

    /// <summary>
    /// Delay before hiding the island when pointer leaves the island boundary (milliseconds).
    /// </summary>
    public int HideDelayMs { get; set; } = 700;

    /// <summary>
    /// Animation transition duration in milliseconds.
    /// </summary>
    public int AnimationDurationMs { get; set; } = 200;

    /// <summary>
    /// Margin around visual island within the window frame to accommodate drop shadows/glow.
    /// </summary>
    public double WindowMargin { get; set; } = 10.0;

    /// <summary>
    /// Total window width (based on maximum potential width plus margins).
    /// </summary>
    public double WindowWidth => Math.Max(CompactWidth, ExpandedWidth) + (WindowMargin * 2);

    /// <summary>
    /// Total window height (based on maximum potential height plus margins).
    /// </summary>
    public double WindowHeight => Math.Max(CompactHeight, ExpandedHeight) + (WindowMargin * 2);
}
