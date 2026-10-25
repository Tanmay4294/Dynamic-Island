namespace Spotifly.App.Services;

public interface ISingleInstanceService : IDisposable
{
    bool IsFirstInstance { get; }
    event EventHandler? SecondInstanceLaunched;
}
