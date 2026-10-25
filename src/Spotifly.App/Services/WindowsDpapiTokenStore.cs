using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spotifly.App.Models;

namespace Spotifly.App.Services;

public interface ISecureTokenStore
{
    SpotifyTokenData? LoadTokens();
    void SaveTokens(SpotifyTokenData tokenData);
    void ClearTokens();
    bool HasTokens();
}

/// <summary>
/// Secure token storage leveraging Windows Data Protection API (DPAPI - CurrentUser scope).
/// Encrypts access and refresh tokens at rest so they are never stored in plain-text.
/// Stored in %LocalAppData%\Spotifly\secure_tokens.dat, excluded from git tracking.
/// </summary>
public sealed class WindowsDpapiTokenStore : ISecureTokenStore
{
    private readonly string _tokenFilePath;
    private readonly ILoggerService? _logger;
    private readonly object _lock = new();

    public WindowsDpapiTokenStore(string? customTokenPath = null, ILoggerService? logger = null)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(customTokenPath))
        {
            _tokenFilePath = customTokenPath;
        }
        else
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spotifly");
            Directory.CreateDirectory(appDataDir);
            _tokenFilePath = Path.Combine(appDataDir, "secure_tokens.dat");
        }
    }

    public bool HasTokens()
    {
        lock (_lock)
        {
            return File.Exists(_tokenFilePath);
        }
    }

    public SpotifyTokenData? LoadTokens()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_tokenFilePath))
                {
                    return null;
                }

                var encryptedBytes = File.ReadAllBytes(_tokenFilePath);
                if (encryptedBytes.Length == 0)
                {
                    return null;
                }

                var decryptedBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser);

                var json = Encoding.UTF8.GetString(decryptedBytes);
                var tokens = JsonSerializer.Deserialize<SpotifyTokenData>(json);
                return tokens;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Failed to decrypt or deserialize secure tokens. Clearing invalid store.", ex);
                ClearTokens();
                return null;
            }
        }
    }

    public void SaveTokens(SpotifyTokenData tokenData)
    {
        ArgumentNullException.ThrowIfNull(tokenData);

        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_tokenFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(tokenData);
                var plainBytes = Encoding.UTF8.GetBytes(json);

                var encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    null,
                    DataProtectionScope.CurrentUser);

                File.WriteAllBytes(_tokenFilePath, encryptedBytes);
                _logger?.LogInformation("Spotify tokens securely encrypted and stored via Windows DPAPI.");
            }
            catch (Exception ex)
            {
                _logger?.LogError("Failed to encrypt and save Spotify tokens.", ex);
                throw;
            }
        }
    }

    public void ClearTokens()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_tokenFilePath))
                {
                    File.Delete(_tokenFilePath);
                }
                _logger?.LogInformation("Spotify tokens cleared from secure store.");
            }
            catch (Exception ex)
            {
                _logger?.LogError("Failed to clear secure token file.", ex);
            }
        }
    }
}
