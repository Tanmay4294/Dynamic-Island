using Microsoft.Win32;

namespace Spotifly.App.Services;

public interface IStartupService
{
    bool IsLaunchAtStartupEnabled();
    bool SetLaunchAtStartup(bool enable);
}

/// <summary>
/// Manages Windows Launch-at-Sign-In via the per-user Registry Run key:
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// Does not require elevation/administrator privileges and is fully idempotent.
/// </summary>
public sealed class WindowsStartupService : IStartupService
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string AppName = "Spotifly";
    private readonly string _runKeySubPath;
    private readonly string _appName;
    private readonly ILoggerService? _logger;

    public WindowsStartupService(ILoggerService? logger = null, string runKeySubPath = RunKeyPath, string appName = AppName)
    {
        _logger = logger;
        _runKeySubPath = runKeySubPath;
        _appName = appName;
    }

    public bool IsLaunchAtStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeySubPath, false);
            var value = key?.GetValue(_appName);
            return value != null;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Error reading Windows Startup registry key.", ex);
            return false;
        }
    }

    public bool SetLaunchAtStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeySubPath, true) 
                         ?? Registry.CurrentUser.CreateSubKey(_runKeySubPath, true);
            if (key == null)
            {
                _logger?.LogWarning("Unable to open HKCU Run key for write access.");
                return false;
            }

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(_appName, $"\"{exePath}\"");
                    _logger?.LogInformation($"Windows startup enabled: {exePath}");
                    return true;
                }
            }
            else
            {
                if (key.GetValue(_appName) != null)
                {
                    key.DeleteValue(_appName, false);
                    _logger?.LogInformation("Windows startup disabled.");
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("Failed to update Windows Startup registry key.", ex);
        }

        return false;
    }
}
