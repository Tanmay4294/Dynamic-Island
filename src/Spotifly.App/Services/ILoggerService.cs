namespace Spotifly.App.Services;

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public interface ILoggerService
{
    void Log(LogLevel level, string message, Exception? exception = null);
    void LogDebug(string message);
    void LogInformation(string message);
    void LogWarning(string message, Exception? exception = null);
    void LogError(string message, Exception? exception = null);
    void LogCritical(string message, Exception? exception = null);
}
