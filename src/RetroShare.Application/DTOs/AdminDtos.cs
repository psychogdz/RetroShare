using System.ComponentModel.DataAnnotations;
using RetroShare.Domain.Constants;

namespace RetroShare.Application.DTOs;

public class AdminUpdateUserRequest
{
    [MinLength(1), MaxLength(64)]
    public string? DisplayName { get; set; }

    public bool? IsDisabled { get; set; }

    /// <summary>New per-user quota in bytes; null leaves the quota unchanged.</summary>
    [Range(0, long.MaxValue)]
    public long? StorageQuotaBytes { get; set; }
}

public class SetUserRolesRequest
{
    [Required]
    public IReadOnlyList<int> RoleIds { get; set; } = [];
}

public class CreateRoleRequest
{
    [Required, MinLength(2), MaxLength(64), RegularExpression(@"^[a-zA-Z0-9 _-]+$")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Description { get; set; } = string.Empty;

    public IReadOnlyList<int> PermissionIds { get; set; } = [];
}

public class UpdateRoleRequest
{
    [MinLength(2), MaxLength(64), RegularExpression(@"^[a-zA-Z0-9 _-]+$")]
    public string? Name { get; set; }

    [MaxLength(256)]
    public string? Description { get; set; }

    /// <summary>When present, replaces the role's permission set entirely.</summary>
    public IReadOnlyList<int>? PermissionIds { get; set; }
}

public sealed class RoleDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public bool IsSystem { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }

    public long UserCount { get; init; }
}

public sealed class PermissionDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required string Category { get; init; }

    public required string Description { get; init; }
}

public sealed class ActivityDto
{
    public required Guid Id { get; init; }

    public required string Action { get; init; }

    public required string Description { get; init; }

    public Guid? UserId { get; init; }

    public string? Username { get; init; }

    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public required DateTime CreatedAt { get; init; }
}

public sealed class DashboardDto
{
    public long TotalFiles { get; init; }

    public long StorageUsedBytes { get; init; }

    public long StorageQuotaBytes { get; init; }

    public double StorageUsedPercent => StorageQuotaBytes <= 0
        ? 0
        : Math.Min(100, StorageUsedBytes * 100.0 / StorageQuotaBytes);

    public long TrashCount { get; init; }

    public long ShareCount { get; init; }

    public required IReadOnlyList<FileDto> RecentFiles { get; init; }

    public required IReadOnlyList<ActivityDto> RecentActivity { get; init; }
}

public sealed class AdminDashboardDto
{
    public long TotalUsers { get; init; }

    public long TotalFiles { get; init; }

    public long TotalBytesStored { get; init; }

    public long TotalShares { get; init; }

    public long ActiveShares { get; init; }

    public required IReadOnlyList<ActivityDto> RecentActivity { get; init; }
}
