using System.Security.Cryptography;
using RetroShare.Application.Interfaces;

namespace RetroShare.Infrastructure.Security;

/// <summary>PBKDF2-SHA256 password hashing (RFC 8018) with per-hash random salt. The stored
/// string is versioned (v1) and embeds iteration count, salt and key so parameters can be
/// raised later without invalidating existing hashes.</summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;
    private const int Iterations = 100_000;
    private const string Version = "v1";

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
        return $"{Version}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string passwordHash, string password)
    {
        if (string.IsNullOrEmpty(passwordHash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || parts[0] != Version)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations is < 1000 or > 10_000_000)
        {
            return false;
        }

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
