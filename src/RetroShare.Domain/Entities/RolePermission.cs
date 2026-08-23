namespace RetroShare.Domain.Entities;

/// <summary>Join entity granting a permission to a role.</summary>
public class RolePermission
{
    public int RoleId { get; set; }

    public int PermissionId { get; set; }

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    public Role Role { get; set; } = null!;

    public Permission Permission { get; set; } = null!;
}
