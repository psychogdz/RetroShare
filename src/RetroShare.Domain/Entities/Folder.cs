namespace RetroShare.Domain.Entities;

/// <summary>A user-owned folder node in a per-owner tree, represented by parent references
/// (never by client-supplied filesystem paths).</summary>
public class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted => DeletedAt is not null;

    public User Owner { get; set; } = null!;

    public Folder? Parent { get; set; }

    public ICollection<Folder> Children { get; set; } = new List<Folder>();

    public ICollection<StoredFile> Files { get; set; } = new List<StoredFile>();
}
