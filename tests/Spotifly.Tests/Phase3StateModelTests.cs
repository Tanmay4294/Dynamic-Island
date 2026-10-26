using System.Windows;
using System.Runtime.ExceptionServices;
using Spotifly.App.Models;
using Spotifly.App.Services;
using Spotifly.App.Views;
using Xunit;

namespace Spotifly.Tests;

[Collection("WPF UI")]
public class Phase3StateModelTests
{
    [Fact]
    public void OverlayDimensions_IncludesPhase3TimingsAndExpandedBounds()
    {
        var dims = new OverlayDimensions();
        Assert.Equal(180.0, dims.CompactWidth);
        Assert.Equal(38.0, dims.CompactHeight);
        Assert.Equal(360.0, dims.ExpandedWidth);
        Assert.Equal(200.0, dims.ExpandedHeight);
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
        Assert.True(Enum.IsDefined(typeof(IslandState), state));
    }

    [Fact]
    public void IslandView_TransitionsThroughStatesOnStaThread()
    {
        RunOnSta(() =>
        {
            var positionService = new OverlayPositionService();
            var window = new IslandView(positionService);
            Assert.Equal(IslandState.Hidden, window.CurrentState);
            IslandState? capturedState = null;
            window.IslandStateChanged += (s, newState) => capturedState = newState;
            window.TransitionToState(IslandState.Compact);
            Assert.Equal(IslandState.Compact, window.CurrentState);
            Assert.Equal(IslandState.Compact, capturedState);
            window.TransitionToState(IslandState.Expanded);
            Assert.Equal(IslandState.Expanded, window.CurrentState);
            Assert.Equal(IslandState.Expanded, capturedState);
            window.TransitionToState(IslandState.SettingsOpen);
            Assert.Equal(IslandState.SettingsOpen, window.CurrentState);
            window.TransitionToState(IslandState.Hidden);
            Assert.Equal(IslandState.Hidden, window.CurrentState);
            window.Close();
        });
    }

    [Fact]
    public void IslandView_TenCursorLeaveAndReentryCycles_CollapseAndReactivate()
    {
        RunOnSta(() =>
        {
            var dims = new OverlayDimensions
            {
                ActivationDelayMs = 50,
                HideDelayMs = 50,
                AnimationDurationMs = 50
            };
            var positionService = new OverlayPositionService(dims);
            var window = new IslandView(positionService);
            var workArea = SystemParameters.WorkArea;
            double centerScreenX = workArea.Left + (workArea.Width / 2.0) + dims.HorizontalOffset;
            double zoneY = workArea.Top + dims.VerticalOffset + (dims.ActivationZoneHeight / 2.0);
            double outsideX = centerScreenX;
            double outsideY = workArea.Top + dims.VerticalOffset + dims.ExpandedHeight + 100.0;
            for (int cycle = 0; cycle < 10; cycle++)
            {
                window.TransitionToState(IslandState.Hidden);
                Assert.Equal(IslandState.Hidden, window.CurrentState);
                window.ProcessCursorPosition(centerScreenX, zoneY);
                window.ActivateFromCursorZone();
                Assert.Equal(IslandState.Compact, window.CurrentState);
                window.TransitionToState(IslandState.Expanded);
                Assert.Equal(IslandState.Expanded, window.CurrentState);
                window.ProcessCursorPosition(outsideX, outsideY);
                window.CollapseAfterCursorLeave();
                Assert.Equal(IslandState.Compact, window.CurrentState);
                window.ProcessCursorPosition(centerScreenX, zoneY);
                window.ActivateFromCursorZone();
                Assert.Equal(IslandState.Compact, window.CurrentState);
                window.TransitionToState(IslandState.Expanded);
                Assert.Equal(IslandState.Expanded, window.CurrentState);
            }
            window.Close();
        });
    }

    [Fact]
    public void IslandView_CursorEntryDuringCollapseAnimation_ReactivatesSmoothly()
    {
        RunOnSta(() =>
        {
            var dims = new OverlayDimensions
            {
                ActivationDelayMs = 50,
                HideDelayMs = 50,
                AnimationDurationMs = 150
            };
            var positionService = new OverlayPositionService(dims);
            var window = new IslandView(positionService);
            var workArea = SystemParameters.WorkArea;
            double centerScreenX = workArea.Left + (workArea.Width / 2.0) + dims.HorizontalOffset;
            double zoneY = workArea.Top + dims.VerticalOffset + (dims.ActivationZoneHeight / 2.0);
            double outsideY = workArea.Top + dims.VerticalOffset + dims.ExpandedHeight + 100.0;
            window.TransitionToState(IslandState.Compact);
            Assert.Equal(IslandState.Compact, window.CurrentState);
            window.ProcessCursorPosition(centerScreenX, outsideY);
            window.CollapseAfterCursorLeave();
            Assert.Equal(IslandState.Compact, window.CurrentState);
            // A pending animation cannot prevent a new activation request.
            window.ProcessCursorPosition(centerScreenX, zoneY);
            window.ActivateFromCursorZone();
            Assert.Equal(IslandState.Compact, window.CurrentState);
            window.TransitionToState(IslandState.Hidden);
            var staleHiddenTransitionVersion = window.CurrentTransitionVersion;
            window.TransitionToState(IslandState.Compact);
            window.CompleteHiddenTransition(staleHiddenTransitionVersion);
            Assert.Equal(IslandState.Compact, window.CurrentState);
            Assert.Equal(Visibility.Visible, window.Visibility);
            window.DismissIsland();
            Assert.Equal(IslandState.Hidden, window.CurrentState);
            window.ProcessCursorPosition(centerScreenX, zoneY);
            window.ActivateFromCursorZone();
            Assert.Equal(IslandState.Compact, window.CurrentState);
            window.Close();
        });
    }

    [Fact]
    public void IslandView_DismissButtonAndTrayToggle_CannotLeaveIslandPermanentlyHidden()
    {
        RunOnSta(() =>
        {
            var dims = new OverlayDimensions();
            var positionService = new OverlayPositionService(dims);
            var window = new IslandView(positionService);
            var workArea = SystemParameters.WorkArea;
            double centerScreenX = workArea.Left + (workArea.Width / 2.0) + dims.HorizontalOffset;
            double zoneY = workArea.Top + dims.VerticalOffset + (dims.ActivationZoneHeight / 2.0);
            window.ToggleVisibility();
            Assert.Equal(IslandState.Compact, window.CurrentState);
            window.ToggleVisibility();
            Assert.Equal(IslandState.Hidden, window.CurrentState);
            window.ProcessCursorPosition(centerScreenX, zoneY);
            window.ActivateFromCursorZone();
            Assert.Equal(IslandState.Compact, window.CurrentState);
            window.Close();
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA test thread timed out.");
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
