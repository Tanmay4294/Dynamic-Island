using Spotifly.App.Models;

namespace Spotifly.App.Services;

public interface IOverlayPositionService
{
    OverlayDimensions Dimensions { get; }
    (double Left, double Top) CalculateWindowPosition(double windowWidth, double windowHeight, double screenWidth, double screenTop, double screenLeft);
}
