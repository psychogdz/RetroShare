namespace RetroShare.Domain.Entities;

/// <summary>Metadata for a stored file. The physical blob lives on disk under a generated
/// internal name; <see cref="Name"/> is the user-facing, sanitized display name.</summary>
public class StoredFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }

    /// <summary>Sanitized display name shown to the user (original filename at upload).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Generated opaque storage name (GUID) — never derived from user input.</summary>
    public string StoredName { get; set; } = string.Empty;

    /// <summary>Relative path under the storage root, e.g. "users/{ownerId}/{fileId}".</summary>
    public string StoragePath { get; set; } = string.Empty;

    public long Size { get; set; }

    public string MimeType { get; set; } = "application/octet-stream";

    public string Extension { get; set; } = string.Empty;

    public Guid? FolderId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted => DeletedAt is not null;

    public long DownloadCount { get; set; }

    public User Owner { get; set; } = null!;

    public Folder? Folder { get; set; }

    public ICollection<ShareLink> Shares { get; set; } = new List<ShareLink>();
}
