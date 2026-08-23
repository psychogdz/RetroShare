namespace RetroShare.Domain.Entities;

/// <summary>A public share link for a single file. The token is a cryptographically random
/// string — sequential IDs are never exposed.</summary>
public class ShareLink
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FileId { get; set; }

    /// <summary>URL-safe random token generated with a cryptographic RNG.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash of the optional share password. Null when the share is public.</summary>
    public string? PasswordHash { get; set; }

    public bool HasPassword => PasswordHash is not null;

    public DateTime? ExpiresAt { get; set; }

    /// <summary>Maximum allowed downloads; null means unlimited.</summary>
    public int? MaxDownloads { get; set; }

    public int DownloadCount { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid CreatedBy { get; set; }

    public StoredFile File { get; set; } = null!;

    /// <summary>Evaluates link usability at a point in time (enabled, not expired, under the
    /// download limit). The owning file's deleted state is checked by the service layer.</summary>
    public bool IsUsable(DateTime utcNow) =>
        IsActive
        && (ExpiresAt is null || ExpiresAt > utcNow)
        && (MaxDownloads is null || DownloadCount < MaxDownloads.Value);
}
