using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Spotifly.App.Services;

/// <summary>
/// Provides thread-safe structured file logging with automatic size limit and file rotation.
/// Strictly scrubs sensitive tokens, secrets, codes, and credential keys from log messages.
/// </summary>
public sealed class FileLoggerService : ILoggerService
{
    private readonly string _logDirectory;
    private readonly string _primaryLogPath;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxArchivedFiles;
    private readonly object _lock = new();

    // Sensitive patterns to scrub
    private static readonly Regex BearerTokenRegex = new(@"(Bearer\s+)[A-Za-z0-9_\-\.]{10,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecretKeyValueRegex = new(@"((?:client_secret|access_token|refresh_token|code|verifier|secret|password)\s*[:=]\s*[""']?)[^""'\s,\}]+([""']?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public FileLoggerService(string? customDirectory = null, long maxFileSizeBytes = 2 * 1024 * 1024, int maxArchivedFiles = 3)
    {
        _logDirectory = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spotifly",
            "Logs");

        Directory.CreateDirectory(_logDirectory);
        _primaryLogPath = Path.Combine(_logDirectory, "spotifly.log");
        _maxFileSizeBytes = maxFileSizeBytes;
        _maxArchivedFiles = maxArchivedFiles;
    }

    public string LogDirectory => _logDirectory;
    public string PrimaryLogPath => _primaryLogPath;

    public void LogDebug(string message) => Log(LogLevel.Debug, message);
    public void LogInformation(string message) => Log(LogLevel.Information, message);
    public void LogWarning(string message, Exception? exception = null) => Log(LogLevel.Warning, message, exception);
    public void LogError(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
    public void LogCritical(string message, Exception? exception = null) => Log(LogLevel.Critical, message, exception);

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        try
        {
            var sanitizedMessage = Sanitize(message);
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'");
            var sb = new StringBuilder();
            sb.Append($"[{timestamp}] [{level,-11}] {sanitizedMessage}");

            if (exception != null)
            {
                var sanitizedEx = Sanitize(exception.ToString());
                sb.AppendLine();
                sb.Append($"[Exception] {sanitizedEx}");
            }

            var line = sb.ToString();

            lock (_lock)
            {
                RotateIfNeeded();
                File.AppendAllText(_primaryLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the host application
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_primaryLogPath))
            {
                return;
            }

            var fileInfo = new FileInfo(_primaryLogPath);
            if (fileInfo.Length < _maxFileSizeBytes)
            {
                return;
            }

            // Rotate archives: .3 deleted, .2 -> .3, .1 -> .2, primary -> .1
            for (int i = _maxArchivedFiles - 1; i >= 1; i--)
            {
                var src = Path.Combine(_logDirectory, $"spotifly.{i}.log");
                var dst = Path.Combine(_logDirectory, $"spotifly.{i + 1}.log");
                if (File.Exists(src))
                {
                    if (File.Exists(dst))
                    {
                        File.Delete(dst);
                    }
                    File.Move(src, dst);
                }
            }

            var firstArchive = Path.Combine(_logDirectory, "spotifly.1.log");
            if (File.Exists(firstArchive))
            {
                File.Delete(firstArchive);
            }
            File.Move(_primaryLogPath, firstArchive);
        }
        catch
        {
            // Rotation error fallback: truncate if rename fails
            try
            {
                File.WriteAllText(_primaryLogPath, string.Empty);
            }
            catch { }
        }
    }

    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var res = BearerTokenRegex.Replace(input, "$1[REDACTED]");
        res = SecretKeyValueRegex.Replace(res, "$1[REDACTED]$2");
        return res;
    }
}
