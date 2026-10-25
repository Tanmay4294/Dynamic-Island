using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Spotifly.App.Models;

namespace Spotifly.App.Services;

public interface ISpotifyApiService : IDisposable
{
    Task<SpotifyUserProfile?> GetCurrentUserProfileAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Service communicating with Spotify Web API endpoints.
/// Handles authentication injection, rate limits (HTTP 429 Retry-After), HTTP 401 token refresh,
/// and safe error reporting.
/// </summary>
public sealed class SpotifyApiService : ISpotifyApiService, IDisposable
{
    private readonly ISpotifyAuthService _authService;
    private readonly SpotifyConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILoggerService? _logger;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public SpotifyApiService(
        ISpotifyAuthService authService,
        SpotifyConfig config,
        HttpClient? httpClient = null,
        ILoggerService? logger = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _logger = logger;
    }

    public async Task<SpotifyUserProfile?> GetCurrentUserProfileAsync(CancellationToken cancellationToken = default)
    {
        string? token = await _authService.GetValidAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            _logger?.LogWarning("Cannot execute GET /v1/me: No valid access token available.");
            return null;
        }

        string url = $"{_config.ApiBaseUrl}/me";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized) // 401
            {
                _logger?.LogWarning("Spotify API returned 401 Unauthorized. Retrying once after forced token refresh.");
                // Retry once
                token = await _authService.GetValidAccessTokenAsync(cancellationToken);
                if (string.IsNullOrEmpty(token)) return null;

                using var retryRequest = new HttpRequestMessage(HttpMethod.Get, url);
                retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var retryResponse = await _httpClient.SendAsync(retryRequest, cancellationToken);
                return await HandleResponseAsync(retryResponse, cancellationToken);
            }

            return await HandleResponseAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError("Network error during Spotify API request.", ex);
            throw;
        }
    }

    private async Task<SpotifyUserProfile?> HandleResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == (HttpStatusCode)429) // 429 Too Many Requests
        {
            int retryAfterSec = 5;
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                int.TryParse(values.FirstOrDefault(), out retryAfterSec);
            }
            _logger?.LogWarning($"Spotify rate limit exceeded (HTTP 429). Retry-After: {retryAfterSec}s.");
            throw new InvalidOperationException($"Spotify API rate limit reached. Please wait {retryAfterSec} seconds before retrying.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden) // 403
        {
            _logger?.LogWarning("Spotify API returned 403 Forbidden. User may not be on the developer app allowlist in Development Mode.");
            throw new InvalidOperationException("Spotify account not authorized. In Development Mode, your Spotify email must be added to the Developer Dashboard User Allowlist.");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError($"Spotify API request failed with HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var profile = JsonSerializer.Deserialize<SpotifyUserProfile>(json);
        return profile;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
