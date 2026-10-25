using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Web;
using Spotifly.App.Models;

namespace Spotifly.App.Services;

public interface ISpotifyAuthService
{
    bool IsConnected { get; }
    event EventHandler<bool>? ConnectionStatusChanged;
    Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default);
    Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}

/// <summary>
/// Handles OAuth 2.0 Authorization Code Flow with PKCE (RFC 7636).
/// Starts an HttpListener on the registered loopback IP (127.0.0.1:43821),
/// opens the browser for user consent, validates CSRF state, exchanges code for tokens,
/// and handles token refresh without ever using a client secret.
/// </summary>
public sealed class SpotifyAuthService : ISpotifyAuthService
{
    private readonly SpotifyConfig _config;
    private readonly ISecureTokenStore _tokenStore;
    private readonly HttpClient _httpClient;
    private readonly ILoggerService? _logger;
    private SpotifyTokenData? _currentToken;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public bool IsConnected => _currentToken != null && !string.IsNullOrEmpty(_currentToken.AccessToken);
    public event EventHandler<bool>? ConnectionStatusChanged;

    public SpotifyAuthService(
        SpotifyConfig config,
        ISecureTokenStore tokenStore,
        HttpClient? httpClient = null,
        ILoggerService? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;

        // Attempt to load existing credentials
        _currentToken = _tokenStore.LoadTokens();
    }

    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Starting Spotify OAuth PKCE authorization flow.");

        string codeVerifier = PkceHelper.GenerateCodeVerifier(64);
        string codeChallenge = PkceHelper.GenerateCodeChallenge(codeVerifier);
        string state = PkceHelper.GenerateState(32);

        using var listener = new HttpListener();
        // Listener must listen specifically on loopback prefix
        string prefix = $"http://127.0.0.1:{_config.LoopbackPort}/callback/";
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to start local HttpListener on {prefix}. Port may be occupied.", ex);
            throw new InvalidOperationException($"Could not open local listener on port {_config.LoopbackPort}. Ensure port is available.", ex);
        }

        try
        {
            // Build authorization URL
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["client_id"] = _config.ClientId;
            query["response_type"] = "code";
            query["redirect_uri"] = _config.RedirectUri;
            query["code_challenge_method"] = "S256";
            query["code_challenge"] = codeChallenge;
            query["state"] = state;
            query["scope"] = _config.Scopes;

            string authUrl = $"{_config.AuthorizeEndpoint}?{query}";

            _logger?.LogInformation("Opening system browser for Spotify authorization.");
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            // Wait for callback with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(2)); // 2-minute user consent timeout

            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cts.Token));

            if (completedTask != contextTask)
            {
                throw new TimeoutException("Spotify authorization timed out or was cancelled by the user.");
            }

            var context = await contextTask;
            var request = context.Request;
            var response = context.Response;

            var returnedState = request.QueryString["state"];
            var code = request.QueryString["code"];
            var error = request.QueryString["error"];

            // Send polite user-facing HTML response
            string responseString;
            if (!string.IsNullOrEmpty(error))
            {
                responseString = "<html><body style='font-family:sans-serif;text-align:center;padding:50px;background:#121212;color:#ff6b6b;'>" +
                                 "<h2>Authentication Denied or Failed</h2><p>You can close this window and return to Spotifly.</p></body></html>";
            }
            else
            {
                responseString = "<html><body style='font-family:sans-serif;text-align:center;padding:50px;background:#121212;color:#1ed760;'>" +
                                 "<h2>Spotifly Connected Successfully!</h2><p>You can close this tab and return to the Spotifly Dynamic Island.</p></body></html>";
            }

            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html";
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
            response.OutputStream.Close();

            if (!string.IsNullOrEmpty(error))
            {
                _logger?.LogWarning($"Spotify authorization returned error: {error}");
                return false;
            }

            if (returnedState != state)
            {
                _logger?.LogError("State mismatch detected during OAuth callback. Possible CSRF attack.");
                throw new InvalidOperationException("OAuth state validation failed. Request was rejected for security.");
            }

            if (string.IsNullOrEmpty(code))
            {
                _logger?.LogWarning("No authorization code received in Spotify callback.");
                return false;
            }

            // Exchange Authorization Code + Verifier for Tokens
            return await ExchangeCodeForTokenAsync(code, codeVerifier, cancellationToken);
        }
        finally
        {
            try
            {
                listener.Stop();
            }
            catch { }
        }
    }

    private async Task<bool> ExchangeCodeForTokenAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Exchanging authorization code for Spotify tokens via PKCE.");

        var dict = new Dictionary<string, string>
        {
            { "client_id", _config.ClientId },
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", _config.RedirectUri },
            { "code_verifier", codeVerifier }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _config.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(dict)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError($"Token exchange failed with HTTP {(int)response.StatusCode}. Response: {json}");
            return false;
        }

        var tokenData = JsonSerializer.Deserialize<SpotifyTokenData>(json);
        if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
        {
            _logger?.LogError("Deserialized token data was null or contained empty access token.");
            return false;
        }

        tokenData.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn);
        await SetTokenDataAsync(tokenData);

        _logger?.LogInformation("Spotify PKCE authentication succeeded. Tokens secured.");
        return true;
    }

    public async Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_currentToken == null)
            {
                _currentToken = _tokenStore.LoadTokens();
            }

            if (_currentToken == null)
            {
                return null;
            }

            if (!_currentToken.IsExpired())
            {
                return _currentToken.AccessToken;
            }

            // Access token is expired; refresh using RefreshToken
            if (string.IsNullOrEmpty(_currentToken.RefreshToken))
            {
                _logger?.LogWarning("Access token expired and no refresh token is present. User must re-authenticate.");
                await DisconnectInternalAsync();
                return null;
            }

            bool refreshed = await RefreshAccessTokenAsync(_currentToken.RefreshToken, cancellationToken);
            return refreshed ? _currentToken?.AccessToken : null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<bool> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Refreshing Spotify access token using refresh token.");

        var dict = new Dictionary<string, string>
        {
            { "client_id", _config.ClientId },
            { "grant_type", "refresh_token" },
            { "refresh_token", refreshToken }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _config.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(dict)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogWarning($"Token refresh failed with HTTP {(int)response.StatusCode}. Refresh token may be revoked. Disconnecting.");
            await DisconnectInternalAsync();
            return false;
        }

        var refreshedData = JsonSerializer.Deserialize<SpotifyTokenData>(json);
        if (refreshedData == null || string.IsNullOrEmpty(refreshedData.AccessToken))
        {
            return false;
        }

        // Spotify may or may not return a new refresh_token; if null, keep existing
        if (string.IsNullOrEmpty(refreshedData.RefreshToken))
        {
            refreshedData.RefreshToken = refreshToken;
        }

        refreshedData.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(refreshedData.ExpiresIn);
        await SetTokenDataAsync(refreshedData);

        _logger?.LogInformation("Spotify access token refreshed successfully.");
        return true;
    }

    private async Task SetTokenDataAsync(SpotifyTokenData tokenData)
    {
        _currentToken = tokenData;
        _tokenStore.SaveTokens(tokenData);
        ConnectionStatusChanged?.Invoke(this, true);
        await Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            await DisconnectInternalAsync();
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task DisconnectInternalAsync()
    {
        _currentToken = null;
        _tokenStore.ClearTokens();
        _logger?.LogInformation("Disconnected from Spotify. Local credentials purged.");
        ConnectionStatusChanged?.Invoke(this, false);
        await Task.CompletedTask;
    }
}
