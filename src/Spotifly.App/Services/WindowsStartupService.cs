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
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Spotifly";
    private readonly ILoggerService? _logger;

    public WindowsStartupService(ILoggerService? logger = null)
    {
        _logger = logger;
    }

    public bool IsLaunchAtStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(AppName);
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
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
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
                    key.SetValue(AppName, $"\"{exePath}\"");
                    _logger?.LogInformation($"Windows startup enabled: {exePath}");
                    return true;
                }
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
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
