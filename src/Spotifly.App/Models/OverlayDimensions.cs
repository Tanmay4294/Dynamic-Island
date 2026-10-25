namespace Spotifly.App.Models;

/// <summary>
/// Central configuration model holding dimensions, offsets, and display constraints
/// for the Spotifly Dynamic Island overlay.
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
    /// Corner radius for the bottom corners of the semicircular / pill island.
    /// </summary>
    public double CornerRadius { get; set; } = 26.0;

    /// <summary>
    /// Horizontal offset in DIP from screen center (for webcam alignment tuning).
    /// Positive values shift right; negative values shift left.
    /// </summary>
    public double HorizontalOffset { get; set; } = 0.0;

    /// <summary>
    /// Vertical offset in DIP from the top of the screen work area.
    /// </summary>
    public double VerticalOffset { get; set; } = 0.0;

    /// <summary>
    /// Padding around the visual island within the transparent window frame.
    /// </summary>
    public double WindowMargin { get; set; } = 10.0;

    /// <summary>
    /// Total window width (includes margins for shadows/glow without clipping).
    /// </summary>
    public double WindowWidth => CompactWidth + (WindowMargin * 2);

    /// <summary>
    /// Total window height (includes margins for shadows/glow without clipping).
    /// </summary>
    public double WindowHeight => CompactHeight + (WindowMargin * 2);
}
