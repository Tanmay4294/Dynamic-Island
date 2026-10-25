using System.Threading;
using System.Windows;
using System.Windows.Media;
using Spotifly.App.Controls;
using Spotifly.App.Services;
using Xunit;

namespace Spotifly.Tests;

public class SemicircleProgressControlTests
{
    [Fact]
    public void CalculateArcGeometry_ZeroProgress_ReturnsEmptyFiguresOrZeroSegments()
    {
        var thread = new Thread(() =>
        {
            var geom = SemicircleProgressControl.CalculateArcGeometry(250, 55, 0.0, 1.5, isCompact: true);
            Assert.NotNull(geom);
            Assert.Empty(geom.Figures);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }

    [Fact]
    public void CalculateArcGeometry_FullProgress_ReturnsContinuousArcFromLeftToRight()
    {
        var thread = new Thread(() =>
        {
            double width = 250;
            double height = 55;
            double inset = 1.5;
            var geom = SemicircleProgressControl.CalculateArcGeometry(width, height, 1.0, inset, isCompact: true);

            Assert.NotNull(geom);
            Assert.Single(geom.Figures);
            var figure = geom.Figures[0];
            Assert.False(figure.IsClosed);

            // Start point must be at the left side of the dome (inset, drop + inset)
            double h = height - 2 * inset;
            double drop = h * 0.15;
            Assert.Equal(inset, figure.StartPoint.X, 3);
            Assert.Equal(inset + drop, figure.StartPoint.Y, 3);

            // Arc segment end point must be at the right side of the dome (width - inset, drop + inset)
            Assert.Single(figure.Segments);
            var arc = Assert.IsType<ArcSegment>(figure.Segments[0]);
            Assert.Equal(width - inset, arc.Point.X, 3);
            Assert.Equal(inset + drop, arc.Point.Y, 3);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }

    [Fact]
    public void CalculateArcGeometry_HalfProgress_EndAtApexBottom()
    {
        var thread = new Thread(() =>
        {
            double width = 250;
            double height = 55;
            double inset = 1.5;
            var geom = SemicircleProgressControl.CalculateArcGeometry(width, height, 0.5, inset, isCompact: true);

            Assert.NotNull(geom);
            Assert.Single(geom.Figures);
            var figure = geom.Figures[0];
            Assert.Single(figure.Segments);
            var arc = Assert.IsType<ArcSegment>(figure.Segments[0]);

            // At 50%, arc end point must be at horizontal center (width / 2 = 125)
            Assert.Equal(width / 2.0, arc.Point.X, 2);

            // Vertical position must be near the bottom curve: drop + ry = height - inset
            Assert.Equal(height - inset, arc.Point.Y, 2);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    [InlineData(0.75, 0.75)]
    public void ProgressProperty_CoercesValuesCorrectly(double input, double expected)
    {
        var thread = new Thread(() =>
        {
            var control = new SemicircleProgressControl
            {
                Progress = input
            };
            Assert.Equal(expected, control.Progress, 3);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }

    [Fact]
    public void SimulatedProgressService_AdvancesAndResetsSmoothly()
    {
        var svc = new SimulatedProgressService(null, stepIntervalMs: 50);
        svc.IsAutoSimulating = false;

        svc.SetProgress(0.42);
        Assert.Equal(0.42, svc.Progress, 3);

        svc.NextTrack();
        Assert.Equal(0.0, svc.Progress, 3);

        svc.SetProgress(0.99);
        Assert.Equal(0.99, svc.Progress, 3);

        svc.Reset();
        Assert.Equal(0.0, svc.Progress, 3);
    }

    [Fact]
    public void CalculateTrackGeometry_MatchesExpandedDimensions()
    {
        var thread = new Thread(() =>
        {
            double width = 360;
            double height = 150;
            double inset = 2.0;
            var geom = SemicircleProgressControl.CalculateTrackGeometry(width, height, inset, isCompact: false);

            Assert.NotNull(geom);
            Assert.Single(geom.Figures);
            var figure = geom.Figures[0];

            double h = height - 2 * inset;
            double drop = h * 0.10;
            Assert.Equal(inset, figure.StartPoint.X, 3);
            Assert.Equal(inset + drop, figure.StartPoint.Y, 3);

            var arc = Assert.IsType<ArcSegment>(figure.Segments[0]);
            Assert.Equal(width - inset, arc.Point.X, 3);
            Assert.Equal(inset + drop, arc.Point.Y, 3);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }
}
