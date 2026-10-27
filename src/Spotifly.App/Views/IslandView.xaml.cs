using System.Runtime.InteropServices;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Spotifly.App.Controls;
using Spotifly.App.Models;
using Spotifly.App.Services;

namespace Spotifly.App.Views;

/// <summary>
/// Borderless, transparent, topmost window hosting the semicircular dynamic island.
/// Renders an authentic semicircular/domed vector geometry with a flat top edge flush
/// against the screen and a smoothly curved bottom arc.
/// </summary>
public partial class IslandView : Window
{
    private readonly IOverlayPositionService _positionService;
    private readonly ILoggerService? _logger;
    private readonly ISettingsService? _settingsService;
    private readonly IThemeService? _themeService;
    private readonly IStartupService? _startupService;
    private readonly IAppLifecycleManager? _lifecycleManager;
    private readonly ISimulatedProgressService? _progressService;
    private readonly ISpotifyAuthService? _authService;
    private readonly ISpotifyApiService? _apiService;
    private readonly ISpotifyService? _spotifyService;
    private readonly IWheelNavigationService _wheelNavService;
    private readonly DispatcherTimer _playbackPollTimer;
    private readonly DispatcherTimer _progressAnimationTimer;
    private readonly DispatcherTimer _toastTimer;
    private PlaylistInfo? _selectedPlaylist;
    private double _lastPlaylistCarouselOffset;
    private FanCarouselPanel? _playlistCarouselPanel;
    private bool _isPlaybackRequestInProgress;
    private string? _currentArtworkUrl;
    private int _artworkLoadVersion;
    private long? _liveProgressMs;
    private long? _liveDurationMs;
    private bool _liveIsPlaying;
    private DateTime _liveProgressSnapshotUtc;
    private static readonly HttpClient ArtworkHttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private bool _isDisposed;
    private long _transitionVersion;

    // State machine
    private IslandState _currentState = IslandState.Hidden;
    public IslandState CurrentState => _currentState;
    internal long CurrentTransitionVersion => _transitionVersion;

    // Polling & timer mechanisms for reliable cursor tracking without global hooks
    private readonly DispatcherTimer _cursorPollingTimer;
    private readonly DispatcherTimer _activationTimer;
    private readonly DispatcherTimer _hideTimer;

    private bool _isCursorInsideActivationZone;
    private bool _isCursorInsideIsland;
    private bool _isInteracting;
    private SettingsWindow? _activeSettingsWindow;

    // Dynamic vector geometry cache
    private PathGeometry? _currentIslandGeometry;

    // Win32 constants for click-through and non-activating window management
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);

    private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    public event EventHandler<IslandState>? IslandStateChanged;

    public IslandView(
        IOverlayPositionService positionService,
        ILoggerService? logger = null,
        ISettingsService? settingsService = null,
        IThemeService? themeService = null,
        IStartupService? startupService = null,
        IAppLifecycleManager? lifecycleManager = null,
        ISimulatedProgressService? progressService = null,
        ISpotifyAuthService? authService = null,
        ISpotifyApiService? apiService = null,
        ISpotifyService? spotifyService = null,
        IWheelNavigationService? wheelNavService = null)
    {
        _positionService = positionService;
        _logger = logger;
        _settingsService = settingsService;
        _themeService = themeService;
        _startupService = startupService;
        _lifecycleManager = lifecycleManager;
        _progressService = progressService;
        _authService = authService;
        _apiService = apiService;
        _spotifyService = spotifyService;
        _wheelNavService = wheelNavService ?? new WheelNavigationService();

        InitializeComponent();

        ApplyDimensions();

        if (_settingsService != null)
        {
            _settingsService.SettingsChanged += OnSettingsChanged;
        }

        if (_progressService != null && _spotifyService == null)
        {
            IslandProgressArc.Progress = _progressService.Progress;
            _progressService.ProgressChanged += (s, progress) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    IslandProgressArc.Progress = progress;
                });
            };
        }

        // Spotify playback updates (Phase 7)
        if (_spotifyService != null)
        {
            _spotifyService.PlaybackChanged += OnPlaybackChanged;
            UpdatePlaybackUI(_spotifyService.CurrentPlayback);
        }

        // Lightweight playback poll timer: polls every 3.5 seconds only when island is visible
        _playbackPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _playbackPollTimer.Tick += OnPlaybackPollTick;
        _progressAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _progressAnimationTimer.Tick += OnProgressAnimationTick;
        _progressAnimationTimer.Start();
        if (_authService != null)
        {
            _authService.ConnectionStatusChanged += OnSpotifyConnectionStatusChanged;
            _authService.AuthenticationStateChanged += OnSpotifyAuthenticationStateChanged;
        }

        // Non-blocking toast notification dismissal timer (Phase 10)
        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer.Stop();
            TracksToastBorder.Visibility = Visibility.Collapsed;
        };

        // 1. Activation timer: triggers after cursor stays in top zone for ActivationDelayMs
        _activationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_positionService.Dimensions.ActivationDelayMs)
        };
        _activationTimer.Tick += OnActivationTimerTick;

        // 2. Hide delay timer: triggers after cursor leaves island for HideDelayMs
        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_positionService.Dimensions.HideDelayMs)
        };
        _hideTimer.Tick += OnHideTimerTick;

        // 3. Regular cursor polling (40ms ~ 25 FPS) avoids problematic global low-level mouse hooks
        _cursorPollingTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _cursorPollingTimer.Tick += OnCursorPollTick;

        // Top-level mouse-wheel listener for switching between Now Playing and Library (Phase 11)
        PreviewMouseWheel += IslandView_PreviewMouseWheel;

        // Listen for screen resolution and display changes
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Hover state is driven exclusively by the screen cursor poller. Direct
        // WPF MouseEnter/MouseLeave events are not reliable for this transparent,
        // clipped window during animated resizes and caused left-side flicker.

        // Start hidden by default
        Visibility = Visibility.Hidden;
        _cursorPollingTimer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }

        Reposition();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;
        const int HTCLIENT = 1;

        if (msg == WM_NCHITTEST)
        {
            if (_currentState == IslandState.Hidden || Visibility != Visibility.Visible)
            {
                handled = true;
                return new IntPtr(HTTRANSPARENT);
            }

            int x = (short)(lParam.ToInt64() & 0xFFFF);
            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            var screenPoint = new System.Windows.Point(x, y);

            try
            {
                var pointInWindow = PointFromScreen(screenPoint);

                // Convert window point to IslandContainer coordinate space
                var containerOrigin = IslandContainer.TranslatePoint(new System.Windows.Point(0, 0), this);
                var ptInContainer = new System.Windows.Point(pointInWindow.X - containerOrigin.X, pointInWindow.Y - containerOrigin.Y);

                // Use the same live semicircle region as cursor polling. Keeping
                // native hit-testing and polling identical prevents WPF MouseLeave
                // from firing while the pointer is still over the visible dome.
                if (!IsPointInsideHoverRegion(ptInContainer))
                {
                    handled = true;
                    return new IntPtr(HTTRANSPARENT);
                }

                // Return HTCLIENT for every point inside the island geometry. WPF then
                // performs the normal visual hit test and routes the click to buttons.
                // A second VisualTreeHelper hit test here incorrectly rejected button
                // template visuals on transparent windows on some Windows builds.
                handled = true;
                return new IntPtr(HTCLIENT);
            }
            catch
            {
                // Passthrough on error
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// <summary>
    /// Builds the vector geometry for the island in BOTH compact and expanded modes.
    /// In both states, it renders an authentic semicircular / domed shape with:
    /// - Flat top edge aligned flush against the top screen edge (y = 0)
    /// - Smooth continuous curved bottom contour (ArcSegment) forming the dome
    /// </summary>
    public static PathGeometry CreateIslandGeometry(double width, double height, bool isCompact)
    {
        var figure = new PathFigure
        {
            StartPoint = new System.Windows.Point(0, 0),
            IsClosed = true,
            IsFilled = true
        };

        // Flat top edge: (0, 0) -> (width, 0)
        figure.Segments.Add(new LineSegment(new System.Windows.Point(width, 0), true));

        // Subtle vertical drop before arc initiates (maintains curvature integrity at edges)
        double drop = isCompact ? Math.Max(0, height * 0.15) : Math.Max(0, height * 0.10);
        figure.Segments.Add(new LineSegment(new System.Windows.Point(width, drop), true));

        // Continuous smooth curved bottom arc sweeping to the left side: (width, drop) -> (0, drop)
        figure.Segments.Add(new ArcSegment(
            new System.Windows.Point(0, drop),
            new System.Windows.Size(width / 2.0, height - drop),
            0,
            false,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    public void ApplyDimensions()
    {
        var d = _positionService.Dimensions;
        Width = d.WindowWidth;
        Height = d.WindowHeight;

        IslandContainer.Width = d.CompactWidth;
        IslandContainer.Height = d.CompactHeight;

        UpdateIslandShape(d.CompactWidth, d.CompactHeight, isCompact: true);
    }

    private void UpdateIslandShape(double width, double height, bool isCompact)
    {
        _currentIslandGeometry = CreateIslandGeometry(width, height, isCompact);
        IslandBackgroundPath.Data = _currentIslandGeometry;
        IslandContentContainer.Clip = _currentIslandGeometry;
        if (IslandProgressArc != null)
        {
            IslandProgressArc.IsCompact = isCompact;
            IslandProgressArc.Visibility = Visibility.Visible;
        }

        if (isCompact)
        {
            // Keep the compact Spotify pill clean, without the expanded progress
            // contour or green outline forming an oval around the logo.
            IslandBackgroundPath.Stroke = System.Windows.Media.Brushes.Transparent;
            IslandContainer.Effect = null;
        }
        else
        {
            IslandBackgroundPath.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "IslandBorderBrush");
            IslandContainer.SetResourceReference(UIElement.EffectProperty, "IslandShadowEffect");
        }
    }

    public void Reposition()
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            var (left, top) = _positionService.CalculateWindowPosition(
                Width,
                Height,
                workArea.Width,
                workArea.Top,
                workArea.Left);

            Left = left;
            Top = top;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Failed to reposition IslandView.", ex);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _logger?.LogInformation("Display settings or resolution changed.");
        Dispatcher.InvokeAsync(Reposition);
    }

    private void OnCursorPollTick(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out var pt))
        {
            return;
        }

        ProcessCursorPositionCore(pt.X, pt.Y, coordinatesArePhysicalPixels: true);
    }

    private System.Windows.Point PhysicalScreenPointToDip(double x, double y)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is { } target)
        {
            return target.TransformFromDevice.Transform(new System.Windows.Point(x, y));
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        return new System.Windows.Point(x / dpi.DpiScaleX, y / dpi.DpiScaleY);
    }

    /// <summary>
    /// Processes cursor coordinates against activation zone and island boundaries.
    /// Internal to facilitate automated unit testing of hover activation and hide lifecycles.
    /// </summary>
    internal void ProcessCursorPosition(double cursorX, double cursorY)
    {
        // Tests and callers pass logical screen coordinates. The Win32 poller uses
        // ProcessCursorPositionCore directly with physical screen pixels.
        ProcessCursorPositionCore(cursorX, cursorY, coordinatesArePhysicalPixels: false);
    }

    private void ProcessCursorPositionCore(double cursorX, double cursorY, bool coordinatesArePhysicalPixels)
    {
        var workArea = SystemParameters.WorkArea;
        var dims = _positionService.Dimensions;
        var cursorInDip = coordinatesArePhysicalPixels
            ? PhysicalScreenPointToDip(cursorX, cursorY)
            : new System.Windows.Point(cursorX, cursorY);

        // Check if cursor is in the top-centre activation trigger zone
        double centerScreenX = workArea.Left + (workArea.Width / 2.0) + dims.HorizontalOffset;
        double zoneLeft = centerScreenX - (dims.ActivationZoneWidth / 2.0);
        double zoneRight = centerScreenX + (dims.ActivationZoneWidth / 2.0);
        double zoneTop = workArea.Top + dims.VerticalOffset;
        double zoneBottom = zoneTop + dims.ActivationZoneHeight;

        bool inZone = (cursorInDip.X >= zoneLeft && cursorInDip.X <= zoneRight &&
                       cursorInDip.Y >= zoneTop && cursorInDip.Y <= zoneBottom);

        if (inZone)
        {
            if (!_isCursorInsideActivationZone)
            {
                _isCursorInsideActivationZone = true;
                _hideTimer.Stop();

                if (_currentState == IslandState.Hidden)
                {
                    _activationTimer.Start();
                }
            }
            else
            {
                // Cursor remains inside activation zone; ensure hide timer stays stopped
                _hideTimer.Stop();

                // If currently hidden or animating out and activation timer is not running, start it
                if (_currentState == IslandState.Hidden && !_activationTimer.IsEnabled)
                {
                    _activationTimer.Start();
                }
            }
        }
        else
        {
            if (_isCursorInsideActivationZone)
            {
                _isCursorInsideActivationZone = false;
                _activationTimer.Stop();
            }
        }

        // Check if cursor is currently over the visible island bounds
        if (Visibility == Visibility.Visible && _currentState != IslandState.Hidden)
        {
            try
            {
                var screenPoint = coordinatesArePhysicalPixels
                    ? new System.Windows.Point(cursorX, cursorY)
                    : LogicalScreenPointToPhysical(cursorX, cursorY);
                var ptInWindow = PointFromScreen(screenPoint);
                var containerOrigin = IslandContainer.TranslatePoint(new System.Windows.Point(0, 0), this);
                var ptInContainer = new System.Windows.Point(ptInWindow.X - containerOrigin.X, ptInWindow.Y - containerOrigin.Y);

                // Keep the expanded island open over the whole rendered dome. Use
                // the live animated dimensions here rather than a cached geometry:
                // during a resize the cached path can briefly be one frame ahead of
                // the container, which made a small horizontal mouse move start
                // the hide timer. A small edge tolerance prevents polling jitter
                // from toggling the state at the progress arc.
                bool isOverIsland = IsPointInsideHoverRegion(ptInContainer);

                if (isOverIsland)
                {
                    _hideTimer.Stop();
                    _isCursorInsideIsland = true;

                    // If currently compact and cursor is hovering, expand it
                    if (_currentState == IslandState.Compact && !_isInteracting)
                    {
                        TransitionToState(IslandState.Expanded);
                    }
                }
                else
                {
                    if (_isCursorInsideIsland)
                    {
                        _isCursorInsideIsland = false;
                        StartHideDelayIfNeeded();
                    }
                }
            }
            catch
            {
                // Fallback
            }
        }
    }

    private System.Windows.Point LogicalScreenPointToPhysical(double x, double y)
    {
        var point = new System.Windows.Point(x, y);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is { } target)
        {
            return target.TransformToDevice.Transform(point);
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        return new System.Windows.Point(x * dpi.DpiScaleX, y * dpi.DpiScaleY);
    }

    private bool IsPointInsideHoverRegion(System.Windows.Point point)
    {
        if (_currentIslandGeometry == null)
        {
            return false;
        }

        bool expanded = _currentState is IslandState.Expanded or IslandState.Library or
            IslandState.PlaylistTracks or IslandState.SettingsOpen;
        if (!expanded)
        {
            return _currentIslandGeometry.FillContains(point);
        }

        double width = IslandContainer.ActualWidth;
        double height = IslandContainer.ActualHeight;
        if (width <= 0 || height <= 0 || point.X < 0 || point.X > width || point.Y < 0)
        {
            return false;
        }

        // The flat top of the expanded dome contains the corner action buttons.
        // Keep this header band hit-testable even while the animated arc is being
        // recomputed; otherwise the settings-side button can raise MouseLeave for
        // one polling frame and start the collapse timer.
        // Include a small corner zone around the Settings and Close buttons. The
        // buttons sit above the arc and can briefly straddle the clipped path
        // while the expanded animation is settling, especially on the left side.
        if (point.Y <= 64.0 ||
            ((point.X <= 110.0 || point.X >= width - 110.0) && point.Y <= 120.0))
        {
            return true;
        }

        const double edgeTolerance = 8.0;
        double drop = height * 0.10;
        double radiusX = width / 2.0;
        double radiusY = height - drop;
        double normalizedX = (point.X - radiusX) / radiusX;
        if (Math.Abs(normalizedX) > 1.0)
        {
            return false;
        }

        double curveY = drop + radiusY * Math.Sqrt(Math.Max(0, 1 - normalizedX * normalizedX));
        return point.Y <= curveY + edgeTolerance;
    }

    private void OnActivationTimerTick(object? sender, EventArgs e)
    {
        _activationTimer.Stop();
        ActivateFromCursorZone();
    }

    internal void ActivateFromCursorZone()
    {
        if (_isCursorInsideActivationZone || _isCursorInsideIsland)
        {
            _logger?.LogInformation("Activation timer elapsed: transitioning to Compact.");
            TransitionToState(IslandState.Compact);
        }
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        CollapseAfterCursorLeave();
    }

    internal void CollapseAfterCursorLeave()
    {
        // Do not hide while interacting, while pointer is inside, or while settings window is open
        if (_isCursorInsideIsland || _isCursorInsideActivationZone || _isInteracting || (_activeSettingsWindow != null && _activeSettingsWindow.IsLoaded))
        {
            return;
        }

        _logger?.LogInformation("Hide timer elapsed: collapsing to Compact.");
        TransitionToState(IslandState.Compact);
    }

    private void StartHideDelayIfNeeded()
    {
        if (_currentState == IslandState.Hidden)
        {
            return;
        }

        if (_activeSettingsWindow != null && _activeSettingsWindow.IsLoaded)
        {
            return;
        }

        if (!_hideTimer.IsEnabled)
        {
            _hideTimer.Start();
        }
    }

    private void OnIslandMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hideTimer.Stop();
        _isCursorInsideIsland = true;

        if (_currentState == IslandState.Compact && !_isInteracting)
        {
            TransitionToState(IslandState.Expanded);
        }
    }

    private void OnIslandMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // MouseLeave can be raised during a layout/animation frame even when the
        // cursor is still inside the rendered semicircle. Re-read the actual
        // screen cursor and let the shared containment logic decide whether a
        // hide timer is warranted instead of trusting this routed event alone.
        if (GetCursorPos(out var screenPoint))
        {
            ProcessCursorPositionCore(screenPoint.X, screenPoint.Y, coordinatesArePhysicalPixels: true);
            return;
        }

        _isCursorInsideIsland = false;
        StartHideDelayIfNeeded();
    }

    public void TransitionToState(IslandState newState)
    {
        if (_currentState == newState && Visibility == (newState == IslandState.Hidden ? Visibility.Hidden : Visibility.Visible))
        {
            return;
        }

        _logger?.LogInformation($"Island state transition: {_currentState} -> {newState}");
        var previousState = _currentState;
        _currentState = newState;
        _hideTimer.Stop();
        _activationTimer.Stop();
        var transitionVersion = ++_transitionVersion;
        StopTransitionAnimations();

        AnimateStateTransition(previousState, newState, transitionVersion);
        IslandStateChanged?.Invoke(this, newState);
    }

    private void StopTransitionAnimations()
    {
        var opacity = IslandContainer.Opacity;
        var width = IslandContainer.Width;
        var height = IslandContainer.Height;
        IslandContainer.BeginAnimation(UIElement.OpacityProperty, null);
        IslandContainer.BeginAnimation(FrameworkElement.WidthProperty, null);
        IslandContainer.BeginAnimation(FrameworkElement.HeightProperty, null);
        IslandContainer.Opacity = opacity;
        IslandContainer.Width = width;
        IslandContainer.Height = height;

        foreach (var element in new UIElement[] { CompactView, ExpandedView })
        {
            var currentOpacity = element.Opacity;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = currentOpacity;
        }
    }

    private void AnimateStateTransition(IslandState from, IslandState to, long transitionVersion)
    {
        var dims = _positionService.Dimensions;
        var duration = TimeSpan.FromMilliseconds(dims.AnimationDurationMs);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        if (to == IslandState.Hidden)
        {
            _playbackPollTimer.Stop();
            _progressAnimationTimer.Stop();
            var fadeOut = new DoubleAnimation(IslandContainer.Opacity, 0.0, duration) { EasingFunction = easing };
            fadeOut.Completed += (s, e) => CompleteHiddenTransition(transitionVersion);
            IslandContainer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            return;
        }

        if (Visibility != Visibility.Visible || IslandContainer.Opacity < 1.0)
        {
            Reposition();
            Visibility = Visibility.Visible;
            _playbackPollTimer.Start();
            _progressAnimationTimer.Start();
            _ = _spotifyService?.RefreshPlaybackAsync();
            double currentOpacity = IslandContainer.Opacity;
            // If already fully hidden or starting transition from Hidden, start from 0
            if (from == IslandState.Hidden && currentOpacity >= 1.0)
            {
                currentOpacity = 0.0;
            }
            IslandContainer.Opacity = currentOpacity;
            var fadeIn = new DoubleAnimation(currentOpacity, 1.0, duration) { EasingFunction = easing };
            IslandContainer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        if (to == IslandState.Compact)
        {
            // Animate width & height back to compact and update semicircular geometry
            AnimateIslandDimensions(dims.CompactWidth, dims.CompactHeight, isCompact: true, duration, easing, transitionVersion);

            // Cross-fade views: Compact visible, Expanded collapsed
            CompactView.Visibility = Visibility.Visible;
            var compactFade = new DoubleAnimation(CompactView.Opacity, 1.0, duration) { EasingFunction = easing };
            CompactView.BeginAnimation(UIElement.OpacityProperty, compactFade);

            var expandedFade = new DoubleAnimation(ExpandedView.Opacity, 0.0, duration) { EasingFunction = easing };
            expandedFade.Completed += (s, e) =>
            {
                if (_currentState == IslandState.Compact && _transitionVersion == transitionVersion)
                {
                    ExpandedView.Visibility = Visibility.Collapsed;
                }
            };
            ExpandedView.BeginAnimation(UIElement.OpacityProperty, expandedFade);
        }
        else if (to == IslandState.Expanded || to == IslandState.SettingsOpen || to == IslandState.Library || to == IslandState.PlaylistTracks)
        {
            // Set sub-panel visibility according to target state
            if (to == IslandState.Library)
            {
                ExpandedTitleText.Text = "Playlists";
                ExpandedTitleText.Visibility = Visibility.Visible;
                NavigationHintBadge.Visibility = Visibility.Collapsed;
                NowPlayingPanel.Visibility = Visibility.Collapsed;
                PlaylistTracksPanel.Visibility = Visibility.Collapsed;
                PlaylistsLibraryPanel.Visibility = Visibility.Visible;
                _ = LoadPlaylistsAsync();
            }
            else if (to == IslandState.PlaylistTracks)
            {
                ExpandedTitleText.Visibility = Visibility.Visible;
                ExpandedTitleText.Text = "Songs";
                NavigationHintBadge.Visibility = Visibility.Collapsed; // Wheel mode switching disabled inside tracks
                NowPlayingPanel.Visibility = Visibility.Collapsed;
                PlaylistsLibraryPanel.Visibility = Visibility.Collapsed;
                PlaylistTracksPanel.Visibility = Visibility.Visible;
                if (_selectedPlaylist != null)
                {
                    _ = LoadPlaylistTracksAsync(_selectedPlaylist.Id);
                }
            }
            else
            {
                ExpandedTitleText.Visibility = Visibility.Collapsed;
                NavigationHintBadge.Visibility = Visibility.Collapsed;
                PlaylistsLibraryPanel.Visibility = Visibility.Collapsed;
                PlaylistTracksPanel.Visibility = Visibility.Collapsed;
                NowPlayingPanel.Visibility = Visibility.Visible;
            }

            // Animate width & height to expanded and update expanded stadium geometry
            AnimateIslandDimensions(dims.ExpandedWidth, dims.ExpandedHeight, isCompact: false, duration, easing, transitionVersion);

            // Cross-fade views: Expanded visible, Compact hidden
            ExpandedView.Visibility = Visibility.Visible;
            var expandedFade = new DoubleAnimation(ExpandedView.Opacity, 1.0, duration) { EasingFunction = easing };
            ExpandedView.BeginAnimation(UIElement.OpacityProperty, expandedFade);

            var compactFade = new DoubleAnimation(CompactView.Opacity, 0.0, duration) { EasingFunction = easing };
            compactFade.Completed += (s, e) =>
            {
                if (_currentState != IslandState.Compact && _transitionVersion == transitionVersion)
                {
                    CompactView.Visibility = Visibility.Collapsed;
                }
            };
            CompactView.BeginAnimation(UIElement.OpacityProperty, compactFade);
        }
    }

    internal void CompleteHiddenTransition(long transitionVersion)
    {
        if (_currentState == IslandState.Hidden && _transitionVersion == transitionVersion)
        {
            Visibility = Visibility.Hidden;
            IslandContainer.Opacity = 1.0;
        }
    }

    private void AnimateIslandDimensions(double targetWidth, double targetHeight, bool isCompact, TimeSpan duration, IEasingFunction easing, long transitionVersion)
    {
        double startWidth = IslandContainer.ActualWidth > 0 ? IslandContainer.ActualWidth : targetWidth;
        double startHeight = IslandContainer.ActualHeight > 0 ? IslandContainer.ActualHeight : targetHeight;

        var widthAnim = new DoubleAnimation(startWidth, targetWidth, duration)
        {
            EasingFunction = easing
        };

        var heightAnim = new DoubleAnimation(startHeight, targetHeight, duration)
        {
            EasingFunction = easing
        };

        // Update the geometry at the end of the transition
        heightAnim.Completed += (s, e) =>
        {
            if (_transitionVersion == transitionVersion)
            {
                UpdateIslandShape(targetWidth, targetHeight, isCompact);
            }
        };

        // Immediately update shape structure for target bounds
        UpdateIslandShape(targetWidth, targetHeight, isCompact);

        IslandContainer.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
        IslandContainer.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
    }

    // ==========================================
    // Compact & Header Menu Button Handlers
    // ==========================================

    private void OnSettingsChanged(object? sender, AppSettings newSettings)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _positionService.Dimensions.ActivationDelayMs = newSettings.ActivationDelayMs;
            _positionService.Dimensions.HideDelayMs = newSettings.HideDelayMs;
            _positionService.Dimensions.HorizontalOffset = newSettings.HorizontalOffset;
            _positionService.Dimensions.VerticalOffset = newSettings.VerticalOffset;

            _activationTimer.Interval = TimeSpan.FromMilliseconds(newSettings.ActivationDelayMs);
            _hideTimer.Interval = TimeSpan.FromMilliseconds(newSettings.HideDelayMs);

            Reposition();
        });
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
        e.Handled = true;
    }

    public void OpenSettings()
    {
        _logger?.LogInformation("Settings gear clicked; opening Settings window.");
        _isInteracting = true;
        _hideTimer.Stop();

        try
        {
            if (_activeSettingsWindow == null || !_activeSettingsWindow.IsLoaded)
            {
                var vm = (_settingsService != null && _themeService != null && _startupService != null)
                    ? new ViewModels.SettingsViewModel(
                        _settingsService,
                        _themeService,
                        _startupService,
                        _lifecycleManager,
                        _positionService,
                        _progressService,
                        _authService,
                        _apiService)
                    : null;

                if (vm != null)
                {
                    _activeSettingsWindow = new SettingsWindow(vm)
                    {
                        Owner = this
                    };
                    _activeSettingsWindow.Closed += (s, args) =>
                    {
                        _activeSettingsWindow = null;
                        _isInteracting = false;
                        StartHideDelayIfNeeded();
                    };
                    _activeSettingsWindow.Show();
                    _activeSettingsWindow.Activate();
                    _activeSettingsWindow.Focus();
                    _logger?.LogInformation("Settings window created and activated.");
                    TransitionToState(IslandState.SettingsOpen);
                }
                else
                {
                    _logger?.LogWarning("Settings gear click received, but required Settings services are unavailable.");
                }
            }
            else
            {
                if (_activeSettingsWindow.WindowState == WindowState.Minimized)
                {
                    _activeSettingsWindow.WindowState = WindowState.Normal;
                }
                _activeSettingsWindow.Activate();
                _activeSettingsWindow.Focus();
                _logger?.LogInformation("Existing Settings window activated.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("Error opening SettingsWindow.", ex);
            _isInteracting = false;
        }
    }

    private void OnPlaybackPollTick(object? sender, EventArgs e)
    {
        if (_currentState != IslandState.Hidden && Visibility == Visibility.Visible && _spotifyService != null)
        {
            _ = _spotifyService.RefreshPlaybackAsync();
        }
    }

    private void OnPlaybackChanged(object? sender, PlaybackSnapshot snapshot)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _playbackPollTimer.Interval = snapshot.IsAuthenticated
                ? TimeSpan.FromMilliseconds(3500)
                : TimeSpan.FromSeconds(15);
            UpdatePlaybackUI(snapshot);
        });
    }

    private void OnSpotifyConnectionStatusChanged(object? sender, bool isConnected)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_spotifyService != null && Visibility == Visibility.Visible)
            {
                _ = _spotifyService.RefreshPlaybackAsync();
            }
        });
    }

    private void OnSpotifyAuthenticationStateChanged(object? sender, bool isAuthenticating)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_spotifyService != null && Visibility == Visibility.Visible)
            {
                _ = _spotifyService.RefreshPlaybackAsync();
            }
        });
    }

    private void UpdatePlaybackUI(PlaybackSnapshot snapshot)
    {
        var track = snapshot.Track;

        _liveProgressMs = snapshot.ProgressMs;
        _liveDurationMs = snapshot.DurationMs;
        _liveIsPlaying = snapshot.IsPlaying;
        _liveProgressSnapshotUtc = snapshot.SnapshotTimeUtc;

        // Spotify's playback endpoint provides progress_ms and the track duration.
        // Keep the visual arc driven by live playback whenever that service exists;
        // simulated progress is only used in the settings/demo-only configuration.
        IslandProgressArc.Progress = PlaybackProgressCalculator.Normalize(snapshot.ProgressMs, snapshot.DurationMs);

        TrackTitleText.Text = track.Name;
        TrackArtistText.Text = snapshot.State switch
        {
            SpotifyPlaybackState.Disconnected => "Connect Spotify in Settings",
            SpotifyPlaybackState.Connecting => "Connecting to Spotify...",
            SpotifyPlaybackState.Idle => "Spotify is idle",
            SpotifyPlaybackState.NoActiveDevice => "Open Spotify and start playback",
            SpotifyPlaybackState.Error => snapshot.StatusMessage ?? "Spotify connection needs attention",
            _ => track.ArtistName
        };

        // Play/Pause Icon state: Pause icon when playing, Play triangle when paused
        if (snapshot.IsPlaying)
        {
            // Pause icon (two vertical bars)
            PlayPauseIconPath.Data = Geometry.Parse("M14,19H18V5H14M6,19H10V5H6V19Z");
            PlayPauseButton.ToolTip = "Pause";
        }
        else
        {
            // Play icon (right-pointing triangle)
            PlayPauseIconPath.Data = Geometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z");
            PlayPauseButton.ToolTip = "Play";
        }

        // Active playback device info
        if (!string.IsNullOrEmpty(snapshot.DeviceName))
        {
            PlaybackDeviceText.Text = snapshot.DeviceName;
            PlaybackDeviceText.ToolTip = $"Active Device: {snapshot.DeviceName} ({snapshot.DeviceType ?? "Player"})";
        }
        else
        {
            PlaybackDeviceText.Text = snapshot.State switch
            {
                SpotifyPlaybackState.Disconnected => "Not connected",
                SpotifyPlaybackState.Connecting => "Connecting...",
                SpotifyPlaybackState.Idle => "Idle",
                SpotifyPlaybackState.NoActiveDevice => "No active device",
                SpotifyPlaybackState.Error => "Spotify unavailable",
                _ => snapshot.StatusMessage ?? "Idle"
            };
            PlaybackDeviceText.ToolTip = snapshot.StatusMessage;
        }

        bool canControlPlayback = snapshot.IsAuthenticated && snapshot.HasActiveDevice;
        PreviousTrackButton.IsEnabled = canControlPlayback;
        PlayPauseButton.IsEnabled = canControlPlayback;
        NextTrackButton.IsEnabled = canControlPlayback;
        var controlHint = !snapshot.IsAuthenticated
            ? "Connect Spotify in Settings to enable playback controls."
            : !snapshot.HasActiveDevice
                ? "Start playback on an active Spotify device to enable controls."
                : null;
        PreviousTrackButton.ToolTip = canControlPlayback ? "Previous Track" : controlHint;
        PlayPauseButton.ToolTip = canControlPlayback ? (snapshot.IsPlaying ? "Pause" : "Play") : controlHint;
        NextTrackButton.ToolTip = canControlPlayback ? "Next Track" : controlHint;

        // Album Artwork loading & caching (avoids re-decoding when track artwork hasn't changed)
        if (track.HasArtwork && !string.IsNullOrEmpty(track.ArtworkUrl))
        {
            if (_currentArtworkUrl != track.ArtworkUrl)
            {
                _currentArtworkUrl = track.ArtworkUrl;
                AlbumArtworkImage.Source = null;
                _ = LoadArtworkAsync(track.ArtworkUrl, ++_artworkLoadVersion);
            }
        }
        else
        {
            _currentArtworkUrl = null;
            _artworkLoadVersion++;
            AlbumArtworkImage.Source = null;
        }
    }

    private void OnProgressAnimationTick(object? sender, EventArgs e)
    {
        if (_spotifyService == null || !_liveIsPlaying)
            return;

        var progress = _liveProgressMs.GetValueOrDefault() +
                       (long)Math.Max(0, (DateTime.UtcNow - _liveProgressSnapshotUtc).TotalMilliseconds);
        IslandProgressArc.Progress = PlaybackProgressCalculator.Normalize(progress, _liveDurationMs);
    }

    private async Task LoadArtworkAsync(string artworkUrl, int loadVersion)
    {
        try
        {
            var bytes = await ArtworkHttpClient.GetByteArrayAsync(artworkUrl);
            using var stream = new MemoryStream(bytes);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 160;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            await Dispatcher.InvokeAsync(() =>
            {
                if (loadVersion == _artworkLoadVersion && _currentArtworkUrl == artworkUrl)
                    AlbumArtworkImage.Source = bitmap;
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Spotify album artwork could not be loaded; showing placeholder.", ex);
        }
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        _isInteracting = true;
        _hideTimer.Stop();

        if (_spotifyService != null)
        {
            await _spotifyService.TogglePlayPauseAsync();
        }

        _isInteracting = false;
        StartHideDelayIfNeeded();
    }

    private async void NextTrackButton_Click(object sender, RoutedEventArgs e)
    {
        _isInteracting = true;
        _hideTimer.Stop();

        if (_spotifyService != null)
        {
            await _spotifyService.NextAsync();
        }

        _isInteracting = false;
        StartHideDelayIfNeeded();
    }

    private async void PreviousTrackButton_Click(object sender, RoutedEventArgs e)
    {
        _isInteracting = true;
        _hideTimer.Stop();

        if (_spotifyService != null)
        {
            await _spotifyService.PreviousAsync();
        }

        _isInteracting = false;
        StartHideDelayIfNeeded();
    }

    private void OpenInSpotifyButton_Click(object sender, RoutedEventArgs e)
    {
        _isInteracting = true;
        _hideTimer.Stop();

        var track = _spotifyService?.CurrentPlayback.Track;
        _spotifyService?.OpenSpotifyUriOrWeb(track?.Uri, track?.SpotifyWebUrl);

        _isInteracting = false;
        StartHideDelayIfNeeded();
    }

    private void Artwork_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        OpenInSpotifyButton_Click(sender, e);
    }

    private void LibraryToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isInteracting = true;
        _hideTimer.Stop();

        if (_currentState == IslandState.Library)
        {
            // Switch back to Now Playing expanded view
            TransitionToState(IslandState.Expanded);
        }
        else
        {
            // Switch to Playlists Library view
            TransitionToState(IslandState.Library);
        }

        _isInteracting = false;
        StartHideDelayIfNeeded();
    }

    private async Task LoadPlaylistsAsync(bool forceRefresh = false)
    {
        if (_spotifyService == null) return;

        PlaylistsStatusText.Visibility = Visibility.Visible;
        PlaylistsStatusText.Text = "Loading your playlists...";

        try
        {
            var playlists = await _spotifyService.GetUserPlaylistsAsync(forceRefresh);
            PlaylistsItemsControl.ItemsSource = playlists;
            _playlistCarouselPanel = null;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                GetPlaylistCarouselPanel()?.SetOffset(_lastPlaylistCarouselOffset);
            }));

            if (playlists == null || playlists.Count == 0)
            {
                PlaylistsStatusText.Text = (_authService?.IsConnected ?? false)
                    ? "No playlists found in your library."
                    : "Spotify not connected. Open Settings to connect.";
                PlaylistsStatusText.Visibility = Visibility.Visible;
                PlaylistCountText.Text = "Playlists (0)";
            }
            else
            {
                PlaylistsStatusText.Visibility = Visibility.Collapsed;
                PlaylistCountText.Text = $"Playlists ({playlists.Count})";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("Error loading playlists.", ex);
            PlaylistsStatusText.Text = "Failed to load playlists.";
            PlaylistsStatusText.Visibility = Visibility.Visible;
        }
    }

    private async void RefreshPlaylistsButton_Click(object sender, RoutedEventArgs e)
    {
        _isInteracting = true;
        _hideTimer.Stop();

        await LoadPlaylistsAsync(forceRefresh: true);

        _isInteracting = false;
        StartHideDelayIfNeeded();
    }

    private void PlaylistItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PlaylistInfo playlist)
        {
            _logger?.LogInformation($"Selected playlist: {playlist.Name} ({playlist.Id}). Transitioning to PlaylistTracks state.");
            _selectedPlaylist = playlist;
            _lastPlaylistCarouselOffset = GetPlaylistCarouselPanel()?.Offset ?? _lastPlaylistCarouselOffset;
            ActivePlaylistNameText.Text = playlist.Name;
            TransitionToState(IslandState.PlaylistTracks);
        }
    }

    private async Task LoadPlaylistTracksAsync(string playlistId, bool forceRefresh = false)
    {
        if (_spotifyService == null) return;

        TracksStatusContainer.Visibility = Visibility.Visible;
        TracksStatusText.Text = "Loading songs...";
        RetryTracksButton.Visibility = Visibility.Collapsed;
        TracksItemsControl.ItemsSource = null;

        try
        {
            var tracks = await _spotifyService.GetPlaylistTracksAsync(playlistId, forceRefresh);
            TracksItemsControl.ItemsSource = tracks;

            if (tracks == null || tracks.Count == 0)
            {
                TracksStatusText.Text = "No playable songs found in this playlist.";
                TracksStatusContainer.Visibility = Visibility.Visible;
                RetryTracksButton.Visibility = Visibility.Visible;
            }
            else
            {
                TracksStatusContainer.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error loading songs for playlist {playlistId}.", ex);
            TracksStatusText.Text = "Failed to load playlist songs.";
            TracksStatusContainer.Visibility = Visibility.Visible;
            RetryTracksButton.Visibility = Visibility.Visible;
        }
    }

    private void BackToPlaylistsButton_Click(object sender, RoutedEventArgs e)
    {
        _isInteracting = true;
        _hideTimer.Stop();

        TransitionToState(IslandState.Library);
        // Restore the previous fan position after the track panel is replaced.
        Dispatcher.InvokeAsync(() =>
        {
            GetPlaylistCarouselPanel()?.SetOffset(_lastPlaylistCarouselOffset);
        });

        _isInteracting = false;
        StartHideDelayIfNeeded();
    }

    private async void RefreshTracksButton_Click(object sender, RoutedEventArgs e)
    {
        _isInteracting = true;
        _hideTimer.Stop();

        if (_selectedPlaylist != null)
        {
            await LoadPlaylistTracksAsync(_selectedPlaylist.Id, forceRefresh: true);
        }

        _isInteracting = false;
        StartHideDelayIfNeeded();
    }

    private async void TrackItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is TrackInfo track)
        {
            await PlayTrackInternalAsync(track);
        }
    }

    private async void PlayTrackButton_Click(object sender, RoutedEventArgs e)
    {
        _isInteracting = true;
        _hideTimer.Stop();

        if (sender is FrameworkElement elem && elem.Tag is TrackInfo track)
        {
            await PlayTrackInternalAsync(track);
        }

        _isInteracting = false;
        StartHideDelayIfNeeded();
    }

    private async Task PlayTrackInternalAsync(TrackInfo track)
    {
        if (_spotifyService == null || string.IsNullOrEmpty(track.Uri)) return;

        // Prevent duplicate concurrent requests (Phase 10)
        if (_isPlaybackRequestInProgress)
        {
            _logger?.LogInformation("Playback request already in progress; ignoring duplicate click.");
            return;
        }

        _isPlaybackRequestInProgress = true;
        TracksScrollViewer.IsEnabled = false; // Disable list clicks during request
        ShowToast($"Starting \"{track.Name}\"...", isError: false);

        try
        {
            _logger?.LogInformation($"Initiating playback for track: {track.Name} ({track.Uri}).");
            var result = await _spotifyService.PlayTrackWithResultAsync(track.Uri, _selectedPlaylist?.Uri);

            if (result.IsSuccess)
            {
                ShowToast($"Playing: {track.Name}", isError: false);
                // UX decision documented: Keep user in playlist view without forcibly switching to Now Playing
            }
            else
            {
                _logger?.LogWarning($"Playback attempt failed: {result.Message}");
                ShowToast(result.Message, isError: true);
            }
        }
        finally
        {
            // Restore UI interactivity on success, failure, or cancellation
            _isPlaybackRequestInProgress = false;
            TracksScrollViewer.IsEnabled = true;
        }
    }

    private void ShowToast(string message, bool isError)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _toastTimer.Stop();
            TracksToastText.Text = message;
            TracksToastBorder.BorderBrush = isError
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 107, 107))
                : (System.Windows.Media.Brush)FindResource("IslandAccentBrush");
            TracksToastBorder.Visibility = Visibility.Visible;
            _toastTimer.Start();
        });
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Phase 11: List scroll isolation.
        // Inside playlist and song-list regions, the wheel must scroll the list only.
        // Scroll directly and set e.Handled = true so it NEVER bubbles into the top-level mode switch handler.
        if (sender is System.Windows.Controls.ScrollViewer scroller)
        {
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void PlaylistCarousel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var carousel = GetPlaylistCarouselPanel();
        if (carousel == null || carousel.ItemCount <= 1)
        {
            e.Handled = true;
            return;
        }

        double from = carousel.Offset;
        double to = Math.Clamp(from - e.Delta / 120.0, 0, carousel.ItemCount - 1);
        if (Math.Abs(to - from) > 0.001)
        {
            var motion = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            carousel.BeginAnimation(FanCarouselPanel.OffsetProperty, motion, HandoffBehavior.SnapshotAndReplace);
        }

        e.Handled = true;
    }

    private FanCarouselPanel? GetPlaylistCarouselPanel()
    {
        if (_playlistCarouselPanel != null)
            return _playlistCarouselPanel;

        _playlistCarouselPanel = FindVisualChild<FanCarouselPanel>(PlaylistsItemsControl);
        return _playlistCarouselPanel;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }
        return null;
    }

    private void IslandView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Top-level mouse-wheel navigation (Phase 11):
        // Only active when expanded island is showing Now Playing or Library.
        // If an inner ScrollViewer already handled the event, e.Handled is true and we don't switch.
        if (e.Handled)
        {
            return;
        }

        // Keep wheel gestures over playlist cards separate from top-level view switching.
        if (IsOverScrollList(e.GetPosition(this)))
        {
            return;
        }

        if (_wheelNavService.TryHandleTopLevelWheel(e.Delta, _currentState, out var targetState))
        {
            _logger?.LogInformation($"Top-level wheel gesture detected (delta={e.Delta}). Switching {_currentState} -> {targetState}.");
            TransitionToState(targetState);
            e.Handled = true;
        }
    }

    private bool IsOverScrollList(System.Windows.Point ptInWindow)
    {
        if (PlaylistsLibraryPanel.Visibility == Visibility.Visible && PlaylistsItemsControl.IsVisible)
        {
            var bounds = PlaylistsItemsControl.TransformToAncestor(this).TransformBounds(
                new Rect(0, 0, PlaylistsItemsControl.ActualWidth, PlaylistsItemsControl.ActualHeight));
            if (bounds.Contains(ptInWindow)) return true;
        }

        if (PlaylistTracksPanel.Visibility == Visibility.Visible && TracksScrollViewer.IsVisible)
        {
            var bounds = TracksScrollViewer.TransformToAncestor(this).TransformBounds(
                new Rect(0, 0, TracksScrollViewer.ActualWidth, TracksScrollViewer.ActualHeight));
            if (bounds.Contains(ptInWindow)) return true;
        }

        return false;
    }

    private void OpenPlaylistInSpotify_Click(object sender, RoutedEventArgs e)
    {
        _isInteracting = true;
        _hideTimer.Stop();

        if (sender is FrameworkElement elem && elem.Tag is PlaylistInfo playlist)
        {
            _spotifyService?.OpenSpotifyUriOrWeb(playlist.Uri, playlist.SpotifyWebUrl);
        }

        _isInteracting = false;
        StartHideDelayIfNeeded();
    }

    private void HomeEntryButton_Click(object sender, RoutedEventArgs e)
    {
        _isInteracting = true;
        _hideTimer.Stop();

        _logger?.LogInformation("Spotifly Home/Now Playing entry clicked.");
        TransitionToState(IslandState.Expanded);

        _isInteracting = false;
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        DismissIsland();
    }

    internal void DismissIsland()
    {
        _logger?.LogInformation("Dismiss X clicked: hiding island.");
        _activationTimer.Stop();
        _hideTimer.Stop();
        _isCursorInsideActivationZone = false;
        _isCursorInsideIsland = false;
        _isInteracting = false;
        TransitionToState(IslandState.Hidden);
    }

    public void ToggleVisibility()
    {
        if (_currentState == IslandState.Hidden)
        {
            TransitionToState(IslandState.Compact);
        }
        else
        {
            _activationTimer.Stop();
            _hideTimer.Stop();
            _isCursorInsideActivationZone = false;
            _isCursorInsideIsland = false;
            TransitionToState(IslandState.Hidden);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _cursorPollingTimer.Stop();
            _activationTimer.Stop();
            _hideTimer.Stop();
            _playbackPollTimer.Stop();
            _progressAnimationTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            if (_authService != null)
            {
                _authService.ConnectionStatusChanged -= OnSpotifyConnectionStatusChanged;
                _authService.AuthenticationStateChanged -= OnSpotifyAuthenticationStateChanged;
            }
        }

        base.OnClosed(e);
    }
}
