using System.Security.Claims;
using RetroShare.Domain.Entities;

namespace RetroShare.Application.Interfaces;

/// <summary>PBKDF2 password hashing. Implementations must embed the salt and parameters in
/// the stored string so hashes remain verifiable after parameter upgrades.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}

/// <summary>Cryptographically secure token generation for refresh tokens and share links.</summary>
public interface ISecureTokenGenerator
{
    /// <summary>Generates a URL-safe random token with the requested entropy in bytes.</summary>
    string GenerateToken(int bytes = 32);

    /// <summary>SHA-256 hash (base64) used for at-rest storage of presented tokens.</summary>
    string HashToken(string token);
}

/// <summary>Issues JWT access tokens carrying the user's identity, roles and permissions.</summary>
public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) IssueAccessToken(User user, IEnumerable<string> roleNames,
        IEnumerable<string> permissionNames);
}

/// <summary>Resolves and caches the effective permission set for a user, read live from the
/// database so permission changes apply without re-issuing tokens. Mutating operations call
/// the invalidation methods to make changes take effect immediately.</summary>
public interface IPermissionChecker
{
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>True when the principal is authenticated and currently holds the permission.</summary>
    Task<bool> HasPermissionAsync(ClaimsPrincipal principal, string permission, CancellationToken ct = default);

    /// <summary>Drops the cached permission set of one user (after a role reassignment).</summary>
    void Invalidate(Guid userId);

    /// <summary>Drops every cached permission set (after a role's permissions changed).</summary>
    void InvalidateAll();
}
