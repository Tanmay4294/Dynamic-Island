using System.Collections.Concurrent;
using System.Threading;
using System.Windows;

namespace Spotifly.App.Services;

/// <summary>
/// Manages application shutdown, global cancellation tokens for long-running async tasks,
/// and safe disposal of registered resources.
/// </summary>
public sealed class AppLifecycleManager : IAppLifecycleManager
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentBag<IDisposable> _disposables = new();
    private readonly ILoggerService _logger;
    private int _exitRequested;
    private bool _disposed;

    public AppLifecycleManager(ILoggerService logger)
    {
        _logger = logger;
    }

    public CancellationToken ApplicationStoppingToken
    {
        get
        {
            if (_disposed || _cts.IsCancellationRequested)
            {
                return new CancellationToken(true);
            }

            return _cts.Token;
        }
    }

    public void RegisterDisposable(IDisposable disposable)
    {
        if (disposable != null)
        {
            _disposables.Add(disposable);
        }
    }

    public void RequestExit()
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) == 1)
        {
            return;
        }

        _logger.LogInformation("Application exit explicitly requested.");

        try
        {
            _cts.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError("Error signaling cancellation during application exit.", ex);
        }

        if (System.Windows.Application.Current != null)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.Application.Current.Shutdown(0);
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_cts.IsCancellationRequested)
        {
            try
            {
                _cts.Cancel();
            }
            catch { }
        }

        _cts.Dispose();

        foreach (var item in _disposables)
        {
            try
            {
                item.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error disposing registered resource during shutdown.", ex);
            }
        }
    }
}
