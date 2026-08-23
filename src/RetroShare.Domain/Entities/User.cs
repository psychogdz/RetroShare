namespace RetroShare.Domain.Entities;

/// <summary>A registered account that owns files, folders and share links.</summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Per-user storage quota in bytes. The backend enforces this on upload.</summary>
    public long StorageQuotaBytes { get; set; }

    public bool IsDisabled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    public ICollection<UserRole> Roles { get; set; } = new List<UserRole>();

    public ICollection<StoredFile> Files { get; set; } = new List<StoredFile>();

    public ICollection<Folder> Folders { get; set; } = new List<Folder>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public ICollection<ShareLink> ShareLinks { get; set; } = new List<ShareLink>();
}
