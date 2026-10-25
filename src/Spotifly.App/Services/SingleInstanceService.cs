using System.Threading;

namespace Spotifly.App.Services;

/// <summary>
/// Guarantees that only one instance of the application runs at any time using a named system Mutex
/// and an EventWaitHandle to signal existing instances upon duplicate launches.
/// </summary>
public sealed class SingleInstanceService : ISingleInstanceService
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _eventWaitHandle;
    private readonly RegisteredWaitHandle? _registeredWait;
    private readonly bool _isFirstInstance;
    private bool _disposed;

    public const string DefaultMutexName = "Local\\Spotifly_SingleInstance_Mutex_Tanmay4294";
    public const string DefaultEventName = "Local\\Spotifly_SingleInstance_Signal_Tanmay4294";

    public event EventHandler? SecondInstanceLaunched;

    public bool IsFirstInstance => _isFirstInstance;

    public SingleInstanceService(string mutexName = DefaultMutexName, string eventName = DefaultEventName)
    {
        _mutex = new Mutex(true, mutexName, out _isFirstInstance);

        if (_isFirstInstance)
        {
            _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _eventWaitHandle,
                (state, timedOut) =>
                {
                    if (!timedOut)
                    {
                        SecondInstanceLaunched?.Invoke(this, EventArgs.Empty);
                    }
                },
                null,
                -1,
                false);
        }
        else
        {
            // Signal the primary instance that another launch was attempted
            try
            {
                using var signal = EventWaitHandle.OpenExisting(eventName);
                signal.Set();
            }
            catch
            {
                // If opening the signal fails, exit gracefully without error
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _registeredWait?.Unregister(null);
        _eventWaitHandle?.Dispose();

        if (_isFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex wasn't owned or already released
            }
        }

        _mutex.Dispose();
    }
}
