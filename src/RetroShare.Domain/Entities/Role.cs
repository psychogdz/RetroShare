namespace RetroShare.Domain.Entities;

/// <summary>A named collection of permissions. Roles are pure permission groups and carry no
/// hard-coded meaning in application logic.</summary>
public class Role
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>System roles are seeded and cannot be deleted, so an account always has a
    /// baseline role available. They are not special-cased anywhere else.</summary>
    public bool IsSystem { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();

    public ICollection<UserRole> Users { get; set; } = new List<UserRole>();
}
