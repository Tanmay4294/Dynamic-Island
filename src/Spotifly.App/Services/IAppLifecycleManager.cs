using System.Threading;

namespace Spotifly.App.Services;

public interface IAppLifecycleManager : IDisposable
{
    CancellationToken ApplicationStoppingToken { get; }
    void RequestExit();
    void RegisterDisposable(IDisposable disposable);
}
