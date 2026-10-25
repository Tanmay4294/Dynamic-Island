using System.IO;
using System.Text.Json;
using Spotifly.App.Models;

namespace Spotifly.App.Services;

public interface ISettingsService
{
    AppSettings CurrentSettings { get; }
    event EventHandler<AppSettings>? SettingsChanged;
    void UpdateSettings(Action<AppSettings> updateAction);
    void Save();
    void ResetToDefaults();
}

/// <summary>
/// Thread-safe service for managing and persisting AppSettings in %LocalAppData%\Spotifly\settings.json.
/// Safely falls back to defaults on corrupt, incomplete, or missing configuration files.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private readonly ILoggerService? _logger;
    private readonly object _lock = new();
    private AppSettings _currentSettings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings CurrentSettings
    {
        get
        {
            lock (_lock)
            {
                return _currentSettings.Clone();
            }
        }
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService(string? customSettingsPath = null, ILoggerService? logger = null)
    {
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(customSettingsPath))
        {
            _settingsFilePath = customSettingsPath;
        }
        else
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spotifly");
            Directory.CreateDirectory(appDataDir);
            _settingsFilePath = Path.Combine(appDataDir, "settings.json");
        }

        _currentSettings = LoadInternal();
    }

    private AppSettings LoadInternal()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        settings.ValidateAndSanitize();
                        _logger?.LogInformation("Settings loaded successfully from settings.json.");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Settings file is corrupt or unreadable. Restoring defaults.", ex);
            }

            var defaultSettings = new AppSettings();
            SaveInternal(defaultSettings);
            return defaultSettings;
        }
    }

    public void UpdateSettings(Action<AppSettings> updateAction)
    {
        AppSettings updated;
        lock (_lock)
        {
            updated = _currentSettings.Clone();
            updateAction(updated);
            updated.ValidateAndSanitize();
            _currentSettings = updated;
            SaveInternal(_currentSettings);
        }

        SettingsChanged?.Invoke(this, updated);
    }

    public void Save()
    {
        lock (_lock)
        {
            SaveInternal(_currentSettings);
        }
    }

    public void ResetToDefaults()
    {
        AppSettings defaults = new();
        lock (_lock)
        {
            _currentSettings = defaults;
            SaveInternal(_currentSettings);
        }

        _logger?.LogInformation("Settings reset to defaults.");
        SettingsChanged?.Invoke(this, defaults);
    }

    private void SaveInternal(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = _settingsFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Failed to write settings.json.", ex);
        }
    }
}
