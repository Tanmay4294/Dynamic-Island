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

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        IStartupService startupService,
        IAppLifecycleManager? lifecycleManager = null,
        IOverlayPositionService? positionService = null)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _startupService = startupService;
        _lifecycleManager = lifecycleManager;
        _positionService = positionService;

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
