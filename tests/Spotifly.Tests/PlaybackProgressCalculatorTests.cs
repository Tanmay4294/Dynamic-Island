using Spotifly.App.Services;
using Xunit;

namespace Spotifly.Tests;

public sealed class PlaybackProgressCalculatorTests
{
    [Theory]
    [InlineData(45000L, 200000L, 0.225)]
    [InlineData(0L, 200000L, 0)]
    [InlineData(200000L, 200000L, 1)]
    [InlineData(250000L, 200000L, 1)]
    public void Normalize_MapsPlaybackMillisecondsToArcProgress(long progress, long duration, double expected)
        => Assert.Equal(expected, PlaybackProgressCalculator.Normalize(progress, duration), 6);

    [Fact]
    public void Normalize_MissingOrInvalidDurationResetsToZero()
    {
        Assert.Equal(0, PlaybackProgressCalculator.Normalize(null, 200000));
        Assert.Equal(0, PlaybackProgressCalculator.Normalize(1000, null));
        Assert.Equal(0, PlaybackProgressCalculator.Normalize(1000, 0));
        Assert.Equal(0, PlaybackProgressCalculator.Normalize(-1, 200000));
    }
}
