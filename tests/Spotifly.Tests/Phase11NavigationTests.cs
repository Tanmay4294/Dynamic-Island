using Spotifly.App.Models;
using Spotifly.App.Services;
using Spotifly.App.Views;
using Spotifly.App.Controls;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;

namespace Spotifly.Tests;

[Collection("WPF UI")]
public class Phase11NavigationTests
{
    [Fact]
    public void WheelNavigation_DirectionMapping_ScrollUpInExpanded_NavigatesToLibrary()
    {
        var nav = new WheelNavigationService(debounceMs: 300);

        // Positive delta (scroll up) from Expanded -> Library
        bool switched = nav.TryHandleTopLevelWheel(delta: 120, IslandState.Expanded, out var targetState);

        Assert.True(switched);
        Assert.Equal(IslandState.Library, targetState);
    }

    [Fact]
    public void WheelNavigation_DirectionMapping_ScrollDownInLibrary_NavigatesToExpanded()
    {
        var nav = new WheelNavigationService(debounceMs: 300);

        // Negative delta (scroll down) from Library -> Expanded
        bool switched = nav.TryHandleTopLevelWheel(delta: -120, IslandState.Library, out var targetState);

        Assert.True(switched);
        Assert.Equal(IslandState.Expanded, targetState);
    }

    [Theory]
    [InlineData(IslandState.Expanded, -120)] // Scrolling down while already in Now Playing
    [InlineData(IslandState.Library, 120)]   // Scrolling up while already in Library
    [InlineData(IslandState.Compact, 120)]   // Wheel in Compact mode should not switch
    [InlineData(IslandState.Compact, -120)]
    [InlineData(IslandState.Hidden, 120)]
    [InlineData(IslandState.Hidden, -120)]
    [InlineData(IslandState.PlaylistTracks, 120)] // In playlist tracks, top-level wheel is disabled
    [InlineData(IslandState.PlaylistTracks, -120)]
    [InlineData(IslandState.SettingsOpen, 120)]
    public void WheelNavigation_IgnoresInvalidDirectionsOrStates(IslandState state, int delta)
    {
        var nav = new WheelNavigationService(debounceMs: 300);

        bool switched = nav.TryHandleTopLevelWheel(delta, state, out var targetState);

        Assert.False(switched);
        Assert.Equal(state, targetState);
    }

    [Fact]
    public void WheelNavigation_Debounce_PreventsRapidTouchpadEventBursts()
    {
        var nav = new WheelNavigationService(debounceMs: 350);

        // First deliberate wheel event
        bool first = nav.TryHandleTopLevelWheel(delta: 120, IslandState.Expanded, out var target1);
        Assert.True(first);
        Assert.Equal(IslandState.Library, target1);

        // Immediately following touchpad bursts (within 350ms)
        for (int i = 0; i < 5; i++)
        {
            bool rapidBurst = nav.TryHandleTopLevelWheel(delta: -120, IslandState.Library, out var targetBurst);
            Assert.False(rapidBurst, $"Touchpad burst iteration {i} should be debounced.");
            Assert.Equal(IslandState.Library, targetBurst);
        }

        // After reset or waiting, should allow navigation again
        nav.Reset();
        bool afterCooldown = nav.TryHandleTopLevelWheel(delta: -120, IslandState.Library, out var targetAfter);
        Assert.True(afterCooldown);
        Assert.Equal(IslandState.Expanded, targetAfter);
    }

    [Fact]
    public void WheelNavigation_ZeroDelta_IsIgnored()
    {
        var nav = new WheelNavigationService(debounceMs: 350);

        bool switched = nav.TryHandleTopLevelWheel(delta: 0, IslandState.Expanded, out var target);
        Assert.False(switched);
        Assert.Equal(IslandState.Expanded, target);
    }

    [Fact]
    public void IslandView_MiddleHomeControl_TransitionsToExpandedNowPlaying()
    {
        var thread = new Thread(() =>
        {
            var positionService = new OverlayPositionService();
            var window = new IslandView(positionService);

            // Start in Compact mode
            window.TransitionToState(IslandState.Compact);
            Assert.Equal(IslandState.Compact, window.CurrentState);

            // Locate HomeEntryButton and trigger Click via reflection/method
            var homeBtn = window.FindName("HomeEntryButton") as Button;
            Assert.NotNull(homeBtn);

            // Invoke HomeEntryButton_Click
            var method = typeof(IslandView).GetMethod("HomeEntryButton_Click", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method.Invoke(window, new object[] { homeBtn, new RoutedEventArgs() });

            // Must transition to Expanded (Now Playing)
            Assert.Equal(IslandState.Expanded, window.CurrentState);

            window.Close();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }

    [Fact]
    public void IslandView_BackToPlaylists_RestoresLibraryState()
    {
        var thread = new Thread(() =>
        {
            var positionService = new OverlayPositionService();
            var window = new IslandView(positionService);

            // Simulate selecting a playlist and entering PlaylistTracks
            window.TransitionToState(IslandState.Library);
            Assert.Equal(IslandState.Library, window.CurrentState);

            // Transition to PlaylistTracks
            window.TransitionToState(IslandState.PlaylistTracks);
            Assert.Equal(IslandState.PlaylistTracks, window.CurrentState);

            // Trigger Back button
            var backBtn = window.FindName("BackToPlaylistsButton") as Button;
            Assert.NotNull(backBtn);

            var backMethod = typeof(IslandView).GetMethod("BackToPlaylistsButton_Click", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(backMethod);
            backMethod.Invoke(window, new object[] { backBtn, new RoutedEventArgs() });

            // Must return to Library
            Assert.Equal(IslandState.Library, window.CurrentState);

            window.Close();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }

    [Fact]
    public void IslandView_PlaylistCarousel_HandlesMouseWheelPreventingBubbling()
    {
        var thread = new Thread(() =>
        {
            var positionService = new OverlayPositionService();
            var window = new IslandView(positionService);

            var playlists = window.FindName("PlaylistsItemsControl") as ItemsControl;
            Assert.NotNull(playlists);

            var wheelHandler = typeof(IslandView).GetMethod("PlaylistCarousel_PreviewMouseWheel", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(wheelHandler);

            // Construct MouseWheelEventArgs
            var mouseDevice = Mouse.PrimaryDevice;
            var eventArgs = new MouseWheelEventArgs(mouseDevice, Environment.TickCount, 120)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent
            };

            // Empty carousel still consumes the wheel and prevents a mode switch.
            wheelHandler.Invoke(window, new object[] { playlists, eventArgs });

            // Handled must be true to isolate scrolling and stop bubbling to top-level mode switch
            Assert.True(eventArgs.Handled, "PlaylistCarousel_PreviewMouseWheel must isolate carousel movement from top-level navigation.");

            window.Close();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }

    [Fact]
    public void FanCarouselPanel_ArrangesCenterUprightAndEdgesRotatedOutward()
    {
        var thread = new Thread(() =>
        {
            var panel = new FanCarouselPanel();
            for (int i = 0; i < 5; i++)
                panel.Children.Add(new Border());
            panel.Offset = 2;

            panel.Measure(new Size(300, 108));
            panel.Arrange(new Rect(0, 0, 300, 108));

            var first = Assert.IsType<TransformGroup>(panel.Children[0].RenderTransform);
            var center = Assert.IsType<TransformGroup>(panel.Children[2].RenderTransform);
            var last = Assert.IsType<TransformGroup>(panel.Children[4].RenderTransform);
            Assert.True(Assert.IsType<RotateTransform>(first.Children[1]).Angle < 0);
            Assert.Equal(0, Assert.IsType<RotateTransform>(center.Children[1]).Angle);
            Assert.True(Assert.IsType<RotateTransform>(last.Children[1]).Angle > 0);
            Assert.True(panel.Children[2].Opacity > panel.Children[0].Opacity);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(5000), "WPF carousel layout test thread timed out.");
    }

    [Fact]
    public void IslandView_ClickedPlaylistCard_OpensPlaylistTracks()
    {
        var thread = new Thread(() =>
        {
            var window = new IslandView(new OverlayPositionService());
            window.TransitionToState(IslandState.Library);
            var playlistCard = new Border
            {
                DataContext = new PlaylistInfo
                {
                    Id = "playlist-1",
                    Uri = "spotify:playlist:playlist-1",
                    Name = "Test playlist"
                }
            };

            var handler = typeof(IslandView).GetMethod("PlaylistItem_MouseLeftButtonUp", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(handler);
            handler.Invoke(window, new object[] { playlistCard, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) });

            Assert.Equal(IslandState.PlaylistTracks, window.CurrentState);
            var selected = typeof(IslandView).GetField("_selectedPlaylist", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window);
            Assert.Equal("playlist-1", Assert.IsType<PlaylistInfo>(selected).Id);
            window.Close();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(5000), "WPF playlist selection test thread timed out.");
    }

    [Fact]
    public void IslandView_TopLevelWheel_IgnoresHandledEvents()
    {
        var thread = new Thread(() =>
        {
            var positionService = new OverlayPositionService();
            var navMock = new WheelNavigationService(debounceMs: 0);
            var window = new IslandView(positionService, wheelNavService: navMock);

            window.TransitionToState(IslandState.Expanded);
            Assert.Equal(IslandState.Expanded, window.CurrentState);

            var topWheelHandler = typeof(IslandView).GetMethod("IslandView_PreviewMouseWheel", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(topWheelHandler);

            // Create already handled event
            var eventArgs = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent,
                Handled = true
            };

            topWheelHandler.Invoke(window, new object[] { window, eventArgs });

            // Because it was already handled, state should NOT change
            Assert.Equal(IslandState.Expanded, window.CurrentState);

            window.Close();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }
}
