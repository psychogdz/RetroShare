using RetroShare.Application.DTOs;

using RetroShare.Application.Common;

namespace RetroShare.Application.Interfaces;

public interface IShareService
{
    Task<ShareDto> CreateAsync(Guid userId, CreateShareRequest request, CancellationToken ct = default);

    Task<PagedResult<ShareDto>> ListOwnAsync(Guid userId, int page, int pageSize, CancellationToken ct = default);

    Task<PagedResult<ShareDto>> ListAllAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>Revokes (deactivates) a share. Allowed for the owner or an admin.</summary>
    Task RevokeAsync(Guid shareId, Guid requesterId, bool isAdmin, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>Public share metadata; safe for unauthenticated callers.</summary>
    Task<PublicShareInfoDto> GetPublicInfoAsync(string token, CancellationToken ct = default);

    /// <summary>Validates a share token (and optional password), increments the share's
    /// download counter atomically and opens the blob for streaming.</summary>
    Task<DownloadTicket> AuthorizeShareDownloadAsync(string token, string? password,
        string? ipAddress = null, CancellationToken ct = default);
}

public interface IFolderService
{
    Task<FolderDto> CreateAsync(Guid userId, CreateFolderRequest request, CancellationToken ct = default);

    Task<FolderDto> RenameAsync(Guid folderId, Guid userId, string newName, CancellationToken ct = default);

    Task<FolderDto> MoveAsync(Guid folderId, Guid userId, Guid? targetParentId, CancellationToken ct = default);

    /// <summary>Deletes a folder tree: descendants are soft-deleted, contained files are trashed.</summary>
    Task DeleteAsync(Guid folderId, Guid userId, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>Contents of a folder (or the root when null): breadcrumbs, subfolders, files page.</summary>
    Task<FolderContentsDto> GetContentsAsync(Guid userId, Guid? folderId, string? search, string? typeFilter,
        string sort, bool ascending, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Flat list of all the user's folders, used for move dialogs.</summary>
    Task<IReadOnlyList<FolderDto>> ListAllAsync(Guid userId, CancellationToken ct = default);
}
