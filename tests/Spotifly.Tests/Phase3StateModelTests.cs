using Spotifly.App.Models;
using Spotifly.App.Services;
using Spotifly.App.Views;
using Xunit;

namespace Spotifly.Tests;

public class Phase3StateModelTests
{
    [Fact]
    public void OverlayDimensions_IncludesPhase3TimingsAndExpandedBounds()
    {
        var dims = new OverlayDimensions();

        Assert.Equal(250.0, dims.CompactWidth);
        Assert.Equal(55.0, dims.CompactHeight);
        Assert.Equal(360.0, dims.ExpandedWidth);
        Assert.Equal(150.0, dims.ExpandedHeight);
        Assert.Equal(260.0, dims.ActivationZoneWidth);
        Assert.Equal(6.0, dims.ActivationZoneHeight);
        Assert.Equal(200, dims.ActivationDelayMs);
        Assert.Equal(700, dims.HideDelayMs);
        Assert.Equal(200, dims.AnimationDurationMs);
    }

    [Theory]
    [InlineData(IslandState.Hidden)]
    [InlineData(IslandState.Compact)]
    [InlineData(IslandState.Expanded)]
    [InlineData(IslandState.SettingsOpen)]
    [InlineData(IslandState.Library)]
    [InlineData(IslandState.PlaylistTracks)]
    public void IslandState_DefinesAllRequiredPhase3States(IslandState state)
    {
        // Verifies the enum covers all six required architectural states
        Assert.True(Enum.IsDefined(typeof(IslandState), state));
    }

    [Fact]
    public void IslandView_TransitionsThroughStatesOnStaThread()
    {
        var thread = new Thread(() =>
        {
            var positionService = new OverlayPositionService();
            var window = new IslandView(positionService);

            Assert.Equal(IslandState.Hidden, window.CurrentState);

            IslandState? capturedState = null;
            window.IslandStateChanged += (s, newState) => capturedState = newState;

            // 1. Transition to Compact
            window.TransitionToState(IslandState.Compact);
            Assert.Equal(IslandState.Compact, window.CurrentState);
            Assert.Equal(IslandState.Compact, capturedState);

            // 2. Transition to Expanded
            window.TransitionToState(IslandState.Expanded);
            Assert.Equal(IslandState.Expanded, window.CurrentState);
            Assert.Equal(IslandState.Expanded, capturedState);

            // 3. Transition to SettingsOpen
            window.TransitionToState(IslandState.SettingsOpen);
            Assert.Equal(IslandState.SettingsOpen, window.CurrentState);

            // 4. Transition back to Hidden
            window.TransitionToState(IslandState.Hidden);
            Assert.Equal(IslandState.Hidden, window.CurrentState);

            window.Close();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }
}
