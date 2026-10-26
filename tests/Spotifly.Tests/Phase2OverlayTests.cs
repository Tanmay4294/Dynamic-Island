using Spotifly.App.Models;
using Spotifly.App.Services;
using Xunit;

namespace Spotifly.Tests;

public class Phase2OverlayTests
{
    [Fact]
    public void OverlayDimensions_HasExpectedDefaultValues()
    {
        var dimensions = new OverlayDimensions();

        Assert.Equal(250.0, dimensions.CompactWidth);
        Assert.Equal(55.0, dimensions.CompactHeight);
        Assert.Equal(26.0, dimensions.CornerRadius);
        Assert.Equal(0.0, dimensions.HorizontalOffset);
        Assert.Equal(0.0, dimensions.VerticalOffset);
        Assert.Equal(10.0, dimensions.WindowMargin);
        Assert.Equal(380.0, dimensions.WindowWidth);
        Assert.Equal(220.0, dimensions.WindowHeight);
    }

    [Theory]
    [InlineData(270, 75, 1920, 0, 0, 0, 0, 825, 0)]      // 1920 wide screen, windowWidth 270 -> (1920 - 270)/2 = 825
    [InlineData(270, 75, 2560, 0, 0, 10, 5, 1155, 5)]     // 2560 wide screen, offset X=10, Y=5 -> (2560 - 270)/2 + 10 = 1155, top = 5
    [InlineData(270, 75, 1366, 0, 0, -20, 0, 528, 0)]    // 1366 wide screen, offset X=-20 -> (1366 - 270)/2 - 20 = 528
    public void OverlayPositionService_CalculatesCorrectTopCentreCoordinates(
        double windowWidth,
        double windowHeight,
        double screenWidth,
        double screenTop,
        double screenLeft,
        double offsetX,
        double offsetY,
        double expectedLeft,
        double expectedTop)
    {
        var dimensions = new OverlayDimensions
        {
            CompactWidth = 250,
            WindowMargin = 10,
            HorizontalOffset = offsetX,
            VerticalOffset = offsetY
        };

        var service = new OverlayPositionService(dimensions);
        var (left, top) = service.CalculateWindowPosition(
            windowWidth,
            windowHeight,
            screenWidth,
            screenTop,
            screenLeft);

        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedTop, top);
    }

    [Fact]
    public void OverlayPositionService_AccountsForNonZeroScreenLeftAndTop()
    {
        // For multi-monitor or secondary displays with offset bounds
        var dimensions = new OverlayDimensions
        {
            CompactWidth = 250,
            WindowMargin = 10,
            HorizontalOffset = 0,
            VerticalOffset = 0
        };

        var service = new OverlayPositionService(dimensions);
        var (left, top) = service.CalculateWindowPosition(
            270,
            75,
            1920,
            40, // Taskbar on top or display offset
            1920); // Secondary monitor to the right

        Assert.Equal(1920 + 825, left);
        Assert.Equal(40, top);
    }
}
