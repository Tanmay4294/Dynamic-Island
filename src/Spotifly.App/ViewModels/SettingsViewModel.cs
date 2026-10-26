using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Spotifly.App.Models;
using Spotifly.App.Services;

namespace Spotifly.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly IStartupService _startupService;
    private readonly IAppLifecycleManager? _lifecycleManager;
    private readonly IOverlayPositionService? _positionService;
    private readonly ISimulatedProgressService? _progressService;
    private readonly ISpotifyAuthService? _authService;
    private readonly ISpotifyApiService? _apiService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        IStartupService startupService,
        IAppLifecycleManager? lifecycleManager = null,
        IOverlayPositionService? positionService = null,
        ISimulatedProgressService? progressService = null,
        ISpotifyAuthService? authService = null,
        ISpotifyApiService? apiService = null)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _startupService = startupService;
        _lifecycleManager = lifecycleManager;
        _positionService = positionService;
        _progressService = progressService;
        _authService = authService;
        _apiService = apiService;

        if (_authService != null)
        {
            _authService.ConnectionStatusChanged += (s, isConnected) =>
            {
                OnPropertyChanged(nameof(IsSpotifyConnected));
                OnPropertyChanged(nameof(SpotifyConnectionStatusText));
                if (!isConnected)
                {
                    SpotifyProfileText = "Not connected";
                }
            };

            if (_authService.IsConnected)
            {
                _ = RefreshProfileAsync();
            }
        }

        if (_progressService != null)
        {
            _simulatedProgress = _progressService.Progress;
            _isAutoSimulating = _progressService.IsAutoSimulating;
            _progressService.ProgressChanged += (s, val) =>
            {
                _simulatedProgress = val;
                OnPropertyChanged(nameof(SimulatedProgress));
                OnPropertyChanged(nameof(SimulatedProgressPercentText));
            };
        }

        LoadFromModel(_settingsService.CurrentSettings);
    }

    private void LoadFromModel(AppSettings s)
    {
        _selectedTheme = s.Theme;
        _activationDelayMs = s.ActivationDelayMs;
        _hideDelayMs = s.HideDelayMs;
        _horizontalOffset = s.HorizontalOffset;
        _verticalOffset = s.VerticalOffset;
        _launchAtSignIn = _startupService.IsLaunchAtStartupEnabled();
    }

    private AppTheme _selectedTheme;
    public AppTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (_selectedTheme != value)
            {
                _selectedTheme = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMinimalSelected));
                OnPropertyChanged(nameof(IsModernSelected));
                OnPropertyChanged(nameof(IsGlassmorphismSelected));

                _themeService.ApplyTheme(value);
                _settingsService.UpdateSettings(s => s.Theme = value);
            }
        }
    }

    public bool IsMinimalSelected
    {
        get => SelectedTheme == AppTheme.Minimal;
        set { if (value) SelectedTheme = AppTheme.Minimal; }
    }

    public bool IsModernSelected
    {
        get => SelectedTheme == AppTheme.Modern;
        set { if (value) SelectedTheme = AppTheme.Modern; }
    }

    public bool IsGlassmorphismSelected
    {
        get => SelectedTheme == AppTheme.Glassmorphism;
        set { if (value) SelectedTheme = AppTheme.Glassmorphism; }
    }

    private int _activationDelayMs;
    public int ActivationDelayMs
    {
        get => _activationDelayMs;
        set
        {
            var clamped = Math.Clamp(value, 50, 1500);
            if (_activationDelayMs != clamped)
            {
                _activationDelayMs = clamped;
                OnPropertyChanged();
                _settingsService.UpdateSettings(s => s.ActivationDelayMs = clamped);
                if (_positionService != null)
                {
                    _positionService.Dimensions.ActivationDelayMs = clamped;
                }
            }
        }
    }

    private int _hideDelayMs;
    public int HideDelayMs
    {
        get => _hideDelayMs;
        set
        {
            var clamped = Math.Clamp(value, 200, 3000);
            if (_hideDelayMs != clamped)
            {
                _hideDelayMs = clamped;
                OnPropertyChanged();
                _settingsService.UpdateSettings(s => s.HideDelayMs = clamped);
                if (_positionService != null)
                {
                    _positionService.Dimensions.HideDelayMs = clamped;
                }
            }
        }
    }

    private double _horizontalOffset;
    public double HorizontalOffset
    {
        get => _horizontalOffset;
        set
        {
            var clamped = Math.Clamp(value, -500.0, 500.0);
            if (Math.Abs(_horizontalOffset - clamped) > 0.001)
            {
                _horizontalOffset = clamped;
                OnPropertyChanged();
                _settingsService.UpdateSettings(s => s.HorizontalOffset = clamped);
                if (_positionService != null)
                {
                    _positionService.Dimensions.HorizontalOffset = clamped;
                }
            }
        }
    }

    private double _verticalOffset;
    public double VerticalOffset
    {
        get => _verticalOffset;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 200.0);
            if (Math.Abs(_verticalOffset - clamped) > 0.001)
            {
                _verticalOffset = clamped;
                OnPropertyChanged();
                _settingsService.UpdateSettings(s => s.VerticalOffset = clamped);
                if (_positionService != null)
                {
                    _positionService.Dimensions.VerticalOffset = clamped;
                }
            }
        }
    }

    private bool _launchAtSignIn;
    public bool LaunchAtSignIn
    {
        get => _launchAtSignIn;
        set
        {
            if (_launchAtSignIn != value)
            {
                _launchAtSignIn = value;
                OnPropertyChanged();
                _startupService.SetLaunchAtStartup(value);
                _settingsService.UpdateSettings(s => s.LaunchAtSignIn = value);
            }
        }
    }

    public string AppName => "Spotifly";

    public string AppVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v1.0.0";
        }
    }

    private double _simulatedProgress;
    public double SimulatedProgress
    {
        get => _simulatedProgress;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_simulatedProgress - clamped) > 0.0001)
            {
                _simulatedProgress = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SimulatedProgressPercentText));
                _progressService?.SetProgress(clamped);
            }
        }
    }

    public string SimulatedProgressPercentText => $"{Math.Round(_simulatedProgress * 100):0}%";

    private bool _isAutoSimulating = true;
    public bool IsAutoSimulating
    {
        get => _isAutoSimulating;
        set
        {
            if (_isAutoSimulating != value)
            {
                _isAutoSimulating = value;
                OnPropertyChanged();
                if (_progressService != null)
                {
                    _progressService.IsAutoSimulating = value;
                }
            }
        }
    }

    public void ResetSimulatedProgress()
    {
        _progressService?.Reset();
    }

    public void SimulateNextTrack()
    {
        _progressService?.NextTrack();
    }

    // ==========================================
    // Spotify Connection Properties & Actions (Phase 6)
    // ==========================================

    public bool IsSpotifyConnected => _authService?.IsConnected ?? false;

    public string SpotifyConnectionStatusText => IsSpotifyConnected ? "Connected" : "Disconnected";
    public string SpotifyRedirectUri => _authService?.RedirectUri ?? string.Empty;

    private string _spotifyProfileText = "Not connected";
    public string SpotifyProfileText
    {
        get => _spotifyProfileText;
        private set
        {
            if (_spotifyProfileText != value)
            {
                _spotifyProfileText = value;
                OnPropertyChanged();
            }
        }
    }

    private string? _spotifyStatusMessage;
    public string? SpotifyStatusMessage
    {
        get => _spotifyStatusMessage;
        private set
        {
            if (_spotifyStatusMessage != value)
            {
                _spotifyStatusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isAuthenticating;
    public bool IsAuthenticating
    {
        get => _isAuthenticating;
        private set
        {
            if (_isAuthenticating != value)
            {
                _isAuthenticating = value;
                OnPropertyChanged();
            }
        }
    }

    public async Task ConnectSpotifyAsync()
    {
        if (_authService == null) return;

        IsAuthenticating = true;
        SpotifyStatusMessage = "Waiting for authorization in browser...";

        try
        {
            bool ok = await _authService.AuthenticateAsync();
            if (ok)
            {
                SpotifyStatusMessage = "Connected successfully! Testing API connectivity...";
                await RefreshProfileAsync();
            }
            else
            {
                SpotifyStatusMessage = "Authorization was denied, cancelled, or failed.";
            }
        }
        catch (TimeoutException)
        {
            SpotifyStatusMessage = "Authentication timed out (2 minutes). Please try again.";
        }
        catch (Exception ex)
        {
            SpotifyStatusMessage = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    public async Task DisconnectSpotifyAsync()
    {
        if (_authService == null) return;

        SpotifyStatusMessage = "Disconnecting...";
        try
        {
            await _authService.DisconnectAsync();
            SpotifyStatusMessage = "Disconnected from Spotify.";
            SpotifyProfileText = "Not connected";
        }
        catch (Exception ex)
        {
            SpotifyStatusMessage = $"Error disconnecting: {ex.Message}";
        }
    }

    public async Task RefreshProfileAsync()
    {
        if (_apiService == null || !IsSpotifyConnected) return;

        try
        {
            var profile = await _apiService.GetCurrentUserProfileAsync();
            if (profile != null)
            {
                string plan = string.IsNullOrEmpty(profile.Product) ? "User" : profile.Product.ToUpperInvariant();
                string name = profile.DisplayName ?? profile.Id;
                SpotifyProfileText = $"{name} ({plan})";
                SpotifyStatusMessage = "Spotify connection active and verified via GET /v1/me.";
            }
            else
            {
                if (IsSpotifyConnected)
                {
                    SpotifyStatusMessage = "Spotify credentials are stored, but the profile could not be loaded. Check network access and the Spotify app allowlist.";
                }
                else
                {
                    SpotifyProfileText = "Not connected";
                    SpotifyStatusMessage = "Spotify authorization could not be refreshed. Reconnect Spotify.";
                }
            }
        }
        catch (Exception ex)
        {
            SpotifyStatusMessage = $"API verification error: {ex.Message}";
        }
    }

    public void ResetToDefaults()
    {
        _settingsService.ResetToDefaults();
        var defaults = _settingsService.CurrentSettings;
        LoadFromModel(defaults);

        _themeService.ApplyTheme(defaults.Theme);
        _startupService.SetLaunchAtStartup(defaults.LaunchAtSignIn);

        if (_positionService != null)
        {
            _positionService.Dimensions.ActivationDelayMs = defaults.ActivationDelayMs;
            _positionService.Dimensions.HideDelayMs = defaults.HideDelayMs;
            _positionService.Dimensions.HorizontalOffset = defaults.HorizontalOffset;
            _positionService.Dimensions.VerticalOffset = defaults.VerticalOffset;
        }

        OnPropertyChanged(string.Empty);
    }

    public void ExitApplication()
    {
        _lifecycleManager?.RequestExit();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
