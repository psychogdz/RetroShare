namespace RetroShare.Domain.Entities;

/// <summary>An atomic, independently assignable capability (e.g. <c>files.upload</c>).
/// Permissions live in the database and are granted to roles, never hard-coded.</summary>
public class Permission
{
    public int Id { get; set; }

    /// <summary>Dotted identifier used as the ASP.NET Core authorization policy name.</summary>
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Grouping key for UI display (e.g. "files").</summary>
    public string Category { get; set; } = string.Empty;

    public ICollection<RolePermission> Roles { get; set; } = new List<RolePermission>();
}
