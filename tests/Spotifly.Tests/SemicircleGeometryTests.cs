using System.Windows;
using Spotifly.App.Views;
using Xunit;

namespace Spotifly.Tests;

public class SemicircleGeometryTests
{
    [Fact]
    public void CreateIslandGeometry_CompactMode_ProducesClosedGeometryMatchingSemicircle()
    {
        var thread = new Thread(() =>
        {
            var geometry = IslandView.CreateIslandGeometry(250, 55, isCompact: true);

            Assert.NotNull(geometry);
            Assert.Single(geometry.Figures);
            Assert.True(geometry.Figures[0].IsClosed);
            Assert.True(geometry.Figures[0].IsFilled);

            // Bounds must be within 250 x 55
            Assert.True(geometry.Bounds.Width <= 250.0);
            Assert.True(geometry.Bounds.Height <= 55.0);

            // Centre point inside the dome (125, 25) must be contained in the geometry
            Assert.True(geometry.FillContains(new Point(125, 25)), "Center of semicircle dome must be filled");

            // Corner points near bottom left/right exterior (e.g. 5, 52 or 245, 52) must be outside the curve
            Assert.False(geometry.FillContains(new Point(2, 54)), "Bottom-left exterior corner must not be filled");
            Assert.False(geometry.FillContains(new Point(248, 54)), "Bottom-right exterior corner must not be filled");
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }

    [Fact]
    public void CreateIslandGeometry_ExpandedMode_ProducesClosedSemicircularDomeGeometry()
    {
        var thread = new Thread(() =>
        {
            var geometry = IslandView.CreateIslandGeometry(360, 150, isCompact: false);

            Assert.NotNull(geometry);
            Assert.Single(geometry.Figures);
            Assert.True(geometry.Figures[0].IsClosed);
            Assert.True(geometry.Figures[0].IsFilled);

            // Center of expanded island dome must be filled
            Assert.True(geometry.FillContains(new Point(180, 75)), "Center of expanded island dome must be filled");

            // Extreme bottom corners outside the semicircle contour must not be filled
            Assert.False(geometry.FillContains(new Point(5, 145)), "Expanded bottom-left corner exterior must not be filled");
            Assert.False(geometry.FillContains(new Point(355, 145)), "Expanded bottom-right corner exterior must not be filled");
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }
}
