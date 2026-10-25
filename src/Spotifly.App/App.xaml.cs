using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Spotifly.App.Models;
using Spotifly.App.Services;
using Spotifly.App.Views;

namespace Spotifly.App;

/// <summary>
/// Spotifly Application Lifecycle Entry Point.
/// Configured with ShutdownMode=OnExplicitShutdown, named Mutex single-instance protection,
/// structured rotating logging, and non-intrusive global exception handling.
/// </summary>
public partial class App : System.Windows.Application
{
    private ISingleInstanceService? _singleInstanceService;
    private ILoggerService? _logger;
    private IAppLifecycleManager? _lifecycleManager;
    private IOverlayPositionService? _positionService;
    private ISettingsService? _settingsService;
    private IThemeService? _themeService;
    private IStartupService? _startupService;
    private ISimulatedProgressService? _progressService;
    private TrayIconService? _trayIconService;
    private IslandView? _islandView;

    public static ILoggerService? Logger { get; private set; }
    public static IAppLifecycleManager? Lifecycle { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Initialize Structured Logging first
        _logger = new FileLoggerService();
        Logger = _logger;
        _logger.LogInformation("================== Spotifly Starting Up ==================");

        // 2. Set up Global Exception Boundaries (UI Dispatcher, AppDomain, Unobserved Tasks)
        RegisterExceptionBoundaries();

        // 3. Enforce Single-Instance Execution using named Mutex
        _singleInstanceService = new SingleInstanceService();
        if (!_singleInstanceService.IsFirstInstance)
        {
            _logger.LogInformation("Another Spotifly instance is already running. Signaled existing instance and exiting secondary process safely.");
            _singleInstanceService.Dispose();
            Shutdown(0);
            return;
        }

        // Listen for signals when a second launch occurs
        _singleInstanceService.SecondInstanceLaunched += OnSecondInstanceLaunched;

        // 4. Initialize Lifecycle Manager & Cancellation Token for async tasks
        _lifecycleManager = new AppLifecycleManager(_logger);
        Lifecycle = _lifecycleManager;
        _lifecycleManager.RegisterDisposable(_singleInstanceService);

        // 5. Initialize Core Services: Settings, Theme, and Windows Startup
        _settingsService = new SettingsService(null, _logger);
        _themeService = new ThemeService(_logger);
        _startupService = new WindowsStartupService(_logger);
        _progressService = new SimulatedProgressService(_logger);

        // Load saved theme and apply immediately before opening UI
        var currentSettings = _settingsService.CurrentSettings;
        _themeService.ApplyTheme(currentSettings.Theme);

        // 6. Initialize Overlay Positioning and configure dimensions from persisted settings
        _positionService = new OverlayPositionService();
        _positionService.Dimensions.ActivationDelayMs = currentSettings.ActivationDelayMs;
        _positionService.Dimensions.HideDelayMs = currentSettings.HideDelayMs;
        _positionService.Dimensions.HorizontalOffset = currentSettings.HorizontalOffset;
        _positionService.Dimensions.VerticalOffset = currentSettings.VerticalOffset;

        _islandView = new IslandView(
            _positionService,
            _logger,
            _settingsService,
            _themeService,
            _startupService,
            _lifecycleManager,
            _progressService);
        
        // Window starts hidden as per blueprint specification
        _islandView.Visibility = Visibility.Hidden;

        // 7. Initialize System Tray Icon with Developer Toggle and explicit Exit
        _trayIconService = new TrayIconService(
            onExitRequested: () =>
            {
                _logger.LogInformation("Exit command triggered via tray menu.");
                _lifecycleManager.RequestExit();
            },
            onToggleIslandRequested: () =>
            {
                _logger.LogInformation("Island visibility toggle triggered from tray.");
                _islandView?.ToggleVisibility();
            });
        _lifecycleManager.RegisterDisposable(_trayIconService);

        _logger.LogInformation("Spotifly Phase 4 Shell initialized with ThemeService and Settings.");
    }

    private void OnSecondInstanceLaunched(object? sender, EventArgs e)
    {
        _logger?.LogInformation("Second instance launch attempt detected by primary instance.");
        Dispatcher.InvokeAsync(() =>
        {
            _trayIconService?.ShowNotification(
                "Spotifly",
                "Spotifly is already running in the background. Use the tray icon to toggle the island.",
                System.Windows.Forms.ToolTipIcon.Info);
        });
    }

    private void RegisterExceptionBoundaries()
    {
        // UI Dispatcher unhandled exceptions
        DispatcherUnhandledException += (s, args) =>
        {
            _logger?.LogError("Unhandled UI Dispatcher Exception caught.", args.Exception);
            args.Handled = true;
            ShowNonIntrusiveError("A UI error occurred. Spotifly will continue running.", args.Exception.Message);
        };

        // AppDomain unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                _logger?.LogCritical("Fatal unhandled AppDomain exception caught.", ex);
            }
            else
            {
                _logger?.LogCritical($"Non-exception object thrown: {args.ExceptionObject}");
            }
        };

        // Unobserved task exceptions
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            _logger?.LogError("Unobserved Task Exception caught.", args.Exception);
            args.SetObserved();
        };
    }

    private void ShowNonIntrusiveError(string title, string message)
    {
        try
        {
            _trayIconService?.ShowNotification(title, message, System.Windows.Forms.ToolTipIcon.Warning);
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation($"Spotifly is shutting down with exit code {e.ApplicationExitCode}.");
        
        if (_islandView != null)
        {
            _islandView.Close();
            _islandView = null;
        }

        _lifecycleManager?.Dispose();

        base.OnExit(e);
    }
}
