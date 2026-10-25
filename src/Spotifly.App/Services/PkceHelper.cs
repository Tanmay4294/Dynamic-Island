using System.Security.Cryptography;
using System.Text;

namespace Spotifly.App.Services;

/// <summary>
/// Cryptographic utilities for OAuth 2.0 PKCE (RFC 7636).
/// Generates high-entropy random verifiers, SHA-256 challenges, and CSRF states.
/// </summary>
public static class PkceHelper
{
    private const string UnreservedCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    /// <summary>
    /// Generates a cryptographically secure random verifier string between 43 and 128 characters.
    /// </summary>
    public static string GenerateCodeVerifier(int length = 64)
    {
        if (length < 43 || length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "PKCE verifier length must be between 43 and 128 characters.");
        }

        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            sb.Append(UnreservedCharacters[bytes[i] % UnreservedCharacters.Length]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Computes the Base64-URL-encoded SHA-256 code challenge from a code verifier.
    /// </summary>
    public static string GenerateCodeChallenge(string codeVerifier)
    {
        if (string.IsNullOrEmpty(codeVerifier))
        {
            throw new ArgumentNullException(nameof(codeVerifier));
        }

        var bytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = SHA256.HashData(bytes);
        return Base64UrlEncode(hash);
    }

    /// <summary>
    /// Generates a high-entropy random state string for OAuth CSRF protection.
    /// </summary>
    public static string GenerateState(int length = 32)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Encodes bytes to standard Base64URL without padding.
    /// </summary>
    public static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
