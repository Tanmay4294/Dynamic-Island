using System.Windows;
using Spotifly.App.Models;

namespace Spotifly.App.Services;

/// <summary>
/// Computes overlay window screen coordinates at the top-centre of the active display
/// taking DPI scaling and user configurable offsets into account.
/// </summary>
public sealed class OverlayPositionService : IOverlayPositionService
{
    public OverlayDimensions Dimensions { get; }

    public OverlayPositionService(OverlayDimensions? dimensions = null)
    {
        Dimensions = dimensions ?? new OverlayDimensions();
    }

    public (double Left, double Top) CalculateWindowPosition(
        double windowWidth,
        double windowHeight,
        double screenWidth,
        double screenTop,
        double screenLeft)
    {
        // Top-centre of screen work area + configurable offsets
        // Notice: Window has WindowMargin, so the inner visual island is centered exactly.
        double left = screenLeft + ((screenWidth - windowWidth) / 2.0) + Dimensions.HorizontalOffset;
        double top = screenTop + Dimensions.VerticalOffset;

        return (left, top);
    }
}
