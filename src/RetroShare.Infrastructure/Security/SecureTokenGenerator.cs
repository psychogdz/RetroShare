using System.Security.Cryptography;
using RetroShare.Application.Interfaces;

namespace RetroShare.Infrastructure.Security;

/// <summary>Cryptographically secure token generation. Refresh tokens and share tokens are
/// base64url-encoded random bytes; only SHA-256 hashes are persisted.</summary>
public sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    public string GenerateToken(int bytes = 32)
    {
        var random = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(random)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_'); // base64url — safe in URLs without encoding
    }

    public string HashToken(string token)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }
}
