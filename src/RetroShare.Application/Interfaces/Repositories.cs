using RetroShare.Domain.Entities;
using RetroShare.Domain.Enums;

using RetroShare.Application.Common;

namespace RetroShare.Application.Interfaces;

/// <summary>Parameters for file listing. Owner-scoped when <see cref="OwnerId"/> is set;
/// admin-wide when null. <see cref="TrashOnly"/> selects trashed files instead of active ones.</summary>
public sealed record FileListQuery(
    Guid? OwnerId = null,
    Guid? FolderId = null,
    string? Search = null,
    string? TypeFilter = null,
    string Sort = "createdAt",
    bool Ascending = false,
    int Page = 1,
    int PageSize = 20,
    bool TrashOnly = false,
    bool IncludeDeleted = false);

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);

    Task<User?> GetWithRolesAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<User>> SearchAsync(string? search, bool? disabled, string sort, bool ascending,
        int page, int pageSize, CancellationToken ct = default);

    Task<long> CountAsync(CancellationToken ct = default);

    void Add(User user);

    void Update(User user);

    void Remove(User user);
}

public interface IRoleRepository
{
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Role>> GetAllWithUsersAsync(CancellationToken ct = default);

    Task<Role?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<Role?> GetByIdWithPermissionsAsync(int id, CancellationToken ct = default);

    Task<Role?> GetByNameAsync(string name, CancellationToken ct = default);

    Task<long> CountUsersAsync(int roleId, CancellationToken ct = default);

    void Add(Role role);

    void Update(Role role);

    void Remove(Role role);
}

public interface IPermissionRepository
{
    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Permission>> GetByNamesAsync(IEnumerable<string> names, CancellationToken ct = default);

    Task<Permission?> GetByIdAsync(int id, CancellationToken ct = default);
}

public interface IFileRepository
{
    Task<StoredFile?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<StoredFile?> GetWithSharesAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<StoredFile>> SearchAsync(FileListQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<StoredFile>> ListRecentByOwnerAsync(Guid ownerId, int take, CancellationToken ct = default);

    Task<long> SumActiveBytesAsync(Guid ownerId, CancellationToken ct = default);

    Task<long> CountByOwnerAsync(Guid ownerId, bool includeDeleted, CancellationToken ct = default);

    Task<long> CountSharesByOwnerAsync(Guid ownerId, CancellationToken ct = default);

    Task<long> CountAllFilesAsync(CancellationToken ct = default);

    Task<long> SumAllActiveBytesAsync(CancellationToken ct = default);

    Task<long> CountAllSharesAsync(CancellationToken ct = default);

    Task<long> CountAllUsersAsync(CancellationToken ct = default);

    /// <summary>Bulk-deletes every file row owned by the user (database-level cascade removes
    /// their share links). Returns the number of deleted rows.</summary>
    Task<int> RemoveAllByOwnerAsync(Guid ownerId, CancellationToken ct = default);

    void Add(StoredFile file);

    void Update(StoredFile file);

    void Remove(StoredFile file);
}

public interface IFolderRepository
{
    Task<Folder?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Folder>> ListByOwnerAsync(Guid ownerId, CancellationToken ct = default);

    Task<Folder?> FindByNameAndParentAsync(Guid ownerId, Guid? parentId, string name, CancellationToken ct = default);

    void Add(Folder folder);

    void Update(Folder folder);

    void Remove(Folder folder);
}

public interface IShareRepository
{
    Task<ShareLink?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<ShareLink?> GetByTokenWithFileAsync(string token, CancellationToken ct = default);

    Task<PagedResult<ShareLink>> ListByOwnerAsync(Guid ownerId, int page, int pageSize, CancellationToken ct = default);

    Task<PagedResult<ShareLink>> ListAllAsync(int page, int pageSize, CancellationToken ct = default);

    void Add(ShareLink share);

    void Update(ShareLink share);

    void Remove(ShareLink share);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default);

    Task<int> PurgeExpiredAsync(DateTime utcNow, CancellationToken ct = default);

    void Add(RefreshToken token);

    void Update(RefreshToken token);
}

public interface IActivityRepository
{
    Task<PagedResult<ActivityLog>> ListByUserAsync(Guid userId, int page, int pageSize, CancellationToken ct = default);

    Task<PagedResult<ActivityLog>> ListAllAsync(ActivityAction? action, Guid? userId, int page, int pageSize, CancellationToken ct = default);

    Task AddAsync(ActivityLog entry, CancellationToken ct = default);
}
