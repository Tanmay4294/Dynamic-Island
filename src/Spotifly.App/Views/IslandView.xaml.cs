using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
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
    private readonly DispatcherTimer _playbackPollTimer;
    private bool _isDisposed;

    // State machine
    private IslandState _currentState = IslandState.Hidden;
    public IslandState CurrentState => _currentState;

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
        ISpotifyService? spotifyService = null)
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

        InitializeComponent();

        ApplyDimensions();

        if (_settingsService != null)
        {
            _settingsService.SettingsChanged += OnSettingsChanged;
        }

        if (_progressService != null)
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
            Interval = TimeSpan.FromMilliseconds(3500)
        };
        _playbackPollTimer.Tick += OnPlaybackPollTick;

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

        // Listen for screen resolution and display changes
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Hook MouseEnter / MouseLeave on IslandContainer for robust direct hover detection
        IslandContainer.MouseEnter += OnIslandMouseEnter;
        IslandContainer.MouseLeave += OnIslandMouseLeave;

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

                // Check exact vector geometry hit test
                if (_currentIslandGeometry != null && !_currentIslandGeometry.FillContains(ptInContainer))
                {
                    handled = true;
                    return new IntPtr(HTTRANSPARENT);
                }

                var hitResult = VisualTreeHelper.HitTest(this, pointInWindow);
                if (hitResult == null || hitResult.VisualHit == RootLayout)
                {
                    handled = true;
                    return new IntPtr(HTTRANSPARENT);
                }

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

        var workArea = SystemParameters.WorkArea;
        var dims = _positionService.Dimensions;

        // Check if cursor is in the top-centre activation trigger zone
        double centerScreenX = workArea.Left + (workArea.Width / 2.0) + dims.HorizontalOffset;
        double zoneLeft = centerScreenX - (dims.ActivationZoneWidth / 2.0);
        double zoneRight = centerScreenX + (dims.ActivationZoneWidth / 2.0);
        double zoneTop = workArea.Top + dims.VerticalOffset;
        double zoneBottom = zoneTop + dims.ActivationZoneHeight;

        bool inZone = (pt.X >= zoneLeft && pt.X <= zoneRight && pt.Y >= zoneTop && pt.Y <= zoneBottom);

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
                var ptInWindow = PointFromScreen(new System.Windows.Point(pt.X, pt.Y));
                var containerOrigin = IslandContainer.TranslatePoint(new System.Windows.Point(0, 0), this);
                var ptInContainer = new System.Windows.Point(ptInWindow.X - containerOrigin.X, ptInWindow.Y - containerOrigin.Y);

                bool isOverIsland = _currentIslandGeometry != null && _currentIslandGeometry.FillContains(ptInContainer);

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

    private void OnActivationTimerTick(object? sender, EventArgs e)
    {
        _activationTimer.Stop();
        if (_isCursorInsideActivationZone || _isCursorInsideIsland)
        {
            _logger?.LogInformation("Activation timer elapsed: transitioning to Compact.");
            TransitionToState(IslandState.Compact);
        }
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();

        // Do not hide while interacting, while pointer is inside, or while settings window is open
        if (_isCursorInsideIsland || _isCursorInsideActivationZone || _isInteracting || (_activeSettingsWindow != null && _activeSettingsWindow.IsLoaded))
        {
            return;
        }

        _logger?.LogInformation("Hide timer elapsed: transitioning to Hidden.");
        TransitionToState(IslandState.Hidden);
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

        AnimateStateTransition(previousState, newState);
        IslandStateChanged?.Invoke(this, newState);
    }

    private void AnimateStateTransition(IslandState from, IslandState to)
    {
        var dims = _positionService.Dimensions;
        var duration = TimeSpan.FromMilliseconds(dims.AnimationDurationMs);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        if (to == IslandState.Hidden)
        {
            _playbackPollTimer.Stop();
            var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = easing };
            fadeOut.Completed += (s, e) =>
            {
                if (_currentState == IslandState.Hidden)
                {
                    Visibility = Visibility.Hidden;
                    IslandContainer.Opacity = 1.0;
                }
            };
            IslandContainer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            return;
        }

        if (Visibility != Visibility.Visible)
        {
            Reposition();
            Visibility = Visibility.Visible;
            _playbackPollTimer.Start();
            _ = _spotifyService?.RefreshPlaybackAsync();
            IslandContainer.Opacity = 0.0;
            var fadeIn = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = easing };
            IslandContainer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        if (to == IslandState.Compact)
        {
            // Animate width & height back to compact and update semicircular geometry
            AnimateIslandDimensions(dims.CompactWidth, dims.CompactHeight, isCompact: true, duration, easing);

            // Cross-fade views: Compact visible, Expanded collapsed
            CompactView.Visibility = Visibility.Visible;
            var compactFade = new DoubleAnimation(CompactView.Opacity, 1.0, duration) { EasingFunction = easing };
            CompactView.BeginAnimation(UIElement.OpacityProperty, compactFade);

            var expandedFade = new DoubleAnimation(ExpandedView.Opacity, 0.0, duration) { EasingFunction = easing };
            expandedFade.Completed += (s, e) =>
            {
                if (_currentState == IslandState.Compact)
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
                NowPlayingPanel.Visibility = Visibility.Collapsed;
                PlaylistsLibraryPanel.Visibility = Visibility.Visible;
                _ = LoadPlaylistsAsync();
            }
            else
            {
                ExpandedTitleText.Text = "Now Playing";
                PlaylistsLibraryPanel.Visibility = Visibility.Collapsed;
                NowPlayingPanel.Visibility = Visibility.Visible;
            }

            // Animate width & height to expanded and update expanded stadium geometry
            AnimateIslandDimensions(dims.ExpandedWidth, dims.ExpandedHeight, isCompact: false, duration, easing);

            // Cross-fade views: Expanded visible, Compact hidden
            ExpandedView.Visibility = Visibility.Visible;
            var expandedFade = new DoubleAnimation(ExpandedView.Opacity, 1.0, duration) { EasingFunction = easing };
            ExpandedView.BeginAnimation(UIElement.OpacityProperty, expandedFade);

            var compactFade = new DoubleAnimation(CompactView.Opacity, 0.0, duration) { EasingFunction = easing };
            compactFade.Completed += (s, e) =>
            {
                if (_currentState != IslandState.Compact)
                {
                    CompactView.Visibility = Visibility.Collapsed;
                }
            };
            CompactView.BeginAnimation(UIElement.OpacityProperty, compactFade);
        }
    }

    private void AnimateIslandDimensions(double targetWidth, double targetHeight, bool isCompact, TimeSpan duration, IEasingFunction easing)
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
            UpdateIslandShape(targetWidth, targetHeight, isCompact);
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
                    TransitionToState(IslandState.SettingsOpen);
                }
            }
            else
            {
                _activeSettingsWindow.Activate();
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
        Dispatcher.InvokeAsync(() => UpdatePlaybackUI(snapshot));
    }

    private void UpdatePlaybackUI(PlaybackSnapshot snapshot)
    {
        var track = snapshot.Track;

        TrackTitleText.Text = track.Name;
        TrackArtistText.Text = track.ArtistName;

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
            PlaybackDeviceText.Text = snapshot.StatusMessage ?? "Idle";
            PlaybackDeviceText.ToolTip = snapshot.StatusMessage;
        }

        // Album Artwork loading & caching
        if (track.HasArtwork && !string.IsNullOrEmpty(track.ArtworkUrl))
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(track.ArtworkUrl, UriKind.Absolute);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                AlbumArtworkImage.Source = bitmap;
            }
            catch
            {
                AlbumArtworkImage.Source = null;
            }
        }
        else
        {
            AlbumArtworkImage.Source = null;
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
            // Clean navigation handoff to PlaylistTracks state for Phase 9
            TransitionToState(IslandState.PlaylistTracks);
        }
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
        _logger?.LogInformation("Dismiss X clicked: hiding island.");
        _activationTimer.Stop();
        _hideTimer.Stop();
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
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        }

        base.OnClosed(e);
    }
}
