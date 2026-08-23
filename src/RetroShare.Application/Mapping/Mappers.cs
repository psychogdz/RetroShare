using RetroShare.Application.DTOs;
using RetroShare.Domain.Entities;

namespace RetroShare.Application.Mapping;

/// <summary>Entity → DTO projections. API responses never expose EF entities.</summary>
public static class Mappers
{
    public static FileDto ToDto(this StoredFile file, string? ownerUsername = null) => new()
    {
        Id = file.Id,
        Name = file.Name,
        Size = file.Size,
        MimeType = file.MimeType,
        Extension = file.Extension,
        FolderId = file.FolderId,
        CreatedAt = file.CreatedAt,
        UpdatedAt = file.UpdatedAt,
        DeletedAt = file.DeletedAt,
        IsDeleted = file.DeletedAt is not null,
        DownloadCount = file.DownloadCount,
        ActiveShareCount = file.Shares.Count(s => s.IsActive),
        OwnerUsername = ownerUsername,
    };

    public static FolderDto ToDto(this Folder folder) => new()
    {
        Id = folder.Id,
        Name = folder.Name,
        ParentId = folder.ParentId,
        CreatedAt = folder.CreatedAt,
        UpdatedAt = folder.UpdatedAt,
    };

    public static ShareDto ToDto(this ShareLink share) => new()
    {
        Id = share.Id,
        Token = share.Token,
        FileId = share.FileId,
        FileName = share.File.Name,
        FileSize = share.File.Size,
        ExpiresAt = share.ExpiresAt,
        MaxDownloads = share.MaxDownloads,
        DownloadCount = share.DownloadCount,
        IsActive = share.IsActive,
        HasPassword = share.PasswordHash is not null,
        CreatedAt = share.CreatedAt,
        IsExpired = share.ExpiresAt is not null && share.ExpiresAt <= DateTime.UtcNow,
    };

    public static UserDto ToDto(this User user, IEnumerable<string>? permissions = null, long? storageUsed = null) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        DisplayName = user.DisplayName,
        CreatedAt = user.CreatedAt,
        LastLoginAt = user.LastLoginAt,
        IsDisabled = user.IsDisabled,
        Roles = user.Roles.Select(r => r.Role.Name).OrderBy(n => n).ToList(),
        Permissions = permissions?.OrderBy(p => p).ToList(),
        StorageQuotaBytes = user.StorageQuotaBytes,
        StorageUsedBytes = storageUsed ?? 0,
    };

    public static ActivityDto ToDto(this ActivityLog entry, string? username = null) => new()
    {
        Id = entry.Id,
        Action = entry.Action.ToString(),
        Description = entry.Description,
        UserId = entry.UserId,
        Username = username ?? entry.User?.Username,
        EntityType = entry.EntityType,
        EntityId = entry.EntityId,
        CreatedAt = entry.CreatedAt,
    };

    public static PermissionDto ToDto(this Permission permission) => new()
    {
        Id = permission.Id,
        Name = permission.Name,
        Category = permission.Category,
        Description = permission.Description,
    };

    public static RoleDto ToDto(this Role role, long userCount) => new()
    {
        Id = role.Id,
        Name = role.Name,
        Description = role.Description,
        IsSystem = role.IsSystem,
        Permissions = role.Permissions.Select(p => p.Permission.Name).OrderBy(n => n).ToList(),
        UserCount = userCount,
    };
}
