namespace RetroShare.Domain.Entities;

/// <summary>A revocable refresh token. Only the SHA-256 hash of the token is stored; the raw
/// value exists solely in the HTTP response handed to the client.</summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Base64 SHA-256 hash of the raw token presented by the client.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }

    /// <summary>Hash of the token that replaced this one during rotation, if any.</summary>
    public string? ReplacedByTokenHash { get; set; }

    public string? RevokedReason { get; set; }

    public string? RemoteIpAddress { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;

    public User User { get; set; } = null!;
}
