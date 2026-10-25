using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Spotifly.App.Services;

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
    private TrayIconService? _trayIconService;

    public static ILoggerService? Logger { get; private set; }
    public static IAppLifecycleManager? Lifecycle { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Initialize Structured Logging first so all subsequent errors can be logged safely
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
            // Dispose mutex/handle and exit immediately without initializing UI or trays
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

        // 5. Initialize lightweight System Tray Icon for reliable explicit exit & background presence
        _trayIconService = new TrayIconService(
            onExitRequested: () =>
            {
                _logger.LogInformation("Exit command triggered via tray menu.");
                _lifecycleManager.RequestExit();
            },
            onOpenRequested: () =>
            {
                _logger.LogInformation("Open requested from tray icon.");
                // In Phase 1 no island window is rendered yet, notify user non-intrusively
                _trayIconService?.ShowNotification("Spotifly", "Spotifly background service is running.");
            });
        _lifecycleManager.RegisterDisposable(_trayIconService);

        _logger.LogInformation("Spotifly Phase 1 Shell initialized successfully in background mode.");
    }

    private void OnSecondInstanceLaunched(object? sender, EventArgs e)
    {
        _logger?.LogInformation("Second instance launch attempt detected by primary instance.");
        Dispatcher.InvokeAsync(() =>
        {
            _trayIconService?.ShowNotification(
                "Spotifly",
                "Spotifly is already running in the background.",
                System.Windows.Forms.ToolTipIcon.Info);
        });
    }

    private void RegisterExceptionBoundaries()
    {
        // UI Dispatcher unhandled exceptions
        DispatcherUnhandledException += (s, args) =>
        {
            _logger?.LogError("Unhandled UI Dispatcher Exception caught.", args.Exception);
            
            // Mark as handled to prevent immediate process crash where recoverable
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
            if (_trayIconService != null)
            {
                _trayIconService.ShowNotification(title, message, System.Windows.Forms.ToolTipIcon.Warning);
            }
        }
        catch
        {
            // Fallback: never let error notifications crash the app
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation($"Spotifly is shutting down with exit code {e.ApplicationExitCode}.");
        
        // Clean up all registered disposables and cancel background tasks
        _lifecycleManager?.Dispose();

        base.OnExit(e);
    }
}
