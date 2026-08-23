using Microsoft.EntityFrameworkCore;
using RetroShare.Application.Common;
using RetroShare.Application.Interfaces;
using RetroShare.Domain.Entities;
using RetroShare.Domain.Enums;

namespace RetroShare.Infrastructure.Data;

/// <summary>Extension-category mapping used by the file type filter.</summary>
public static class FileTypeCategories
{
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image"] = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tif", ".tiff"],
        ["document"] = [".pdf", ".doc", ".docx", ".txt", ".md", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".rtf", ".odt", ".json", ".xml", ".yml", ".yaml"],
        ["video"] = [".mp4", ".avi", ".mkv", ".mov", ".webm", ".wmv", ".flv"],
        ["audio"] = [".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac"],
        ["archive"] = [".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz"],
    };

    public static string[] AllExtensions { get; } =
        Map.Values.SelectMany(v => v).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static string[] ExtensionsFor(string category) =>
        Map.TryGetValue(category, out var extensions) ? extensions : [];
}

public class UserRepository(AppDbContext db) : IUserRepository
{
    private DbSet<User> Users => db.Users;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        Users.FirstOrDefaultAsync(u => u.Username == username.ToLowerInvariant(), ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), ct);

    public Task<User?> GetWithRolesAsync(Guid id, CancellationToken ct = default) =>
        Users.Include(u => u.Roles)
            .ThenInclude(ur => ur.Role)
            .ThenInclude(r => r.Permissions)
            .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<PagedResult<User>> SearchAsync(string? search, bool? disabled, string sort,
        bool ascending, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Users.Include(u => u.Roles).ThenInclude(ur => ur.Role).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.Username.Contains(term) || u.Email.Contains(term)
                || u.DisplayName.Contains(term));
        }

        if (disabled.HasValue)
        {
            query = query.Where(u => u.IsDisabled == disabled.Value);
        }

        query = sort.ToLowerInvariant() switch
        {
            "username" => ascending ? query.OrderBy(u => u.Username) : query.OrderByDescending(u => u.Username),
            "email" => ascending ? query.OrderBy(u => u.Email) : query.OrderByDescending(u => u.Email),
            "quota" => ascending ? query.OrderBy(u => u.StorageQuotaBytes) : query.OrderByDescending(u => u.StorageQuotaBytes),
            "lastlogin" => ascending ? query.OrderBy(u => u.LastLoginAt) : query.OrderByDescending(u => u.LastLoginAt),
            _ => ascending ? query.OrderBy(u => u.CreatedAt) : query.OrderByDescending(u => u.CreatedAt),
        };

        var total = await query.LongCountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return PagedResult<User>.Create(items, total, page, pageSize);
    }

    public Task<long> CountAsync(CancellationToken ct = default) =>
        Users.LongCountAsync(ct);

    public void Add(User user) => Users.Add(user);

    public void Update(User user) => Users.Update(user);

    public void Remove(User user) => Users.Remove(user);
}

public class RoleRepository(AppDbContext db) : IRoleRepository
{
    private DbSet<Role> Roles => db.Roles;

    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default) =>
        await Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Role>> GetAllWithUsersAsync(CancellationToken ct = default) =>
        await Roles.Include(r => r.Users)
            .Include(r => r.Permissions).ThenInclude(rp => rp.Permission)
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

    public Task<Role?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Role?> GetByIdWithPermissionsAsync(int id, CancellationToken ct = default) =>
        Roles.Include(r => r.Permissions)
            .ThenInclude(rp => rp.Permission)
            .Include(r => r.Users)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Role?> GetByNameAsync(string name, CancellationToken ct = default) =>
        Roles.Include(r => r.Permissions)
            .ThenInclude(rp => rp.Permission)
            .Include(r => r.Users)
            .FirstOrDefaultAsync(r => r.Name == name, ct);

    public Task<long> CountUsersAsync(int roleId, CancellationToken ct = default) =>
        db.UserRoles.LongCountAsync(ur => ur.RoleId == roleId, ct);

    public void Add(Role role) => Roles.Add(role);

    public void Update(Role role) => Roles.Update(role);

    public void Remove(Role role) => Roles.Remove(role);
}

public class PermissionRepository(AppDbContext db) : IPermissionRepository
{
    public async Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct = default) =>
        await db.Permissions.AsNoTracking()
            .OrderBy(p => p.Category).ThenBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Permission>> GetByNamesAsync(IEnumerable<string> names, CancellationToken ct = default)
    {
        var set = names.ToHashSet(StringComparer.Ordinal);
        return await db.Permissions.AsNoTracking()
            .Where(p => set.Contains(p.Name))
            .ToListAsync(ct);
    }

    public Task<Permission?> GetByIdAsync(int id, CancellationToken ct = default) =>
        db.Permissions.FirstOrDefaultAsync(p => p.Id == id, ct);
}

public class FileRepository(AppDbContext db) : IFileRepository
{
    private DbSet<StoredFile> Files => db.Files;

    public Task<StoredFile?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Files.FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<StoredFile?> GetWithSharesAsync(Guid id, CancellationToken ct = default) =>
        Files.Include(f => f.Shares)
            .Include(f => f.Owner)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<PagedResult<StoredFile>> SearchAsync(FileListQuery query, CancellationToken ct = default)
    {
        var q = Files.Include(f => f.Owner).AsNoTracking().AsQueryable();

        if (query.OwnerId.HasValue)
        {
            q = q.Where(f => f.OwnerId == query.OwnerId.Value);
        }

        if (query.TrashOnly)
        {
            q = q.Where(f => f.DeletedAt != null);
        }
        else if (!query.IncludeDeleted)
        {
            q = q.Where(f => f.DeletedAt == null);
        }

        if (query.FolderId.HasValue && !query.TrashOnly)
        {
            q = q.Where(f => f.FolderId == query.FolderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(f => f.Name.Contains(term) || f.MimeType.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.TypeFilter))
        {
            var extensions = FileTypeCategories.ExtensionsFor(query.TypeFilter);
            if (extensions.Length > 0)
            {
                q = q.Where(f => extensions.Contains(f.Extension));
            }
            else if (string.Equals(query.TypeFilter, "other", StringComparison.OrdinalIgnoreCase))
            {
                var all = FileTypeCategories.AllExtensions;
                q = q.Where(f => !all.Contains(f.Extension));
            }
        }

        q = query.Sort.ToLowerInvariant() switch
        {
            "name" => query.Ascending ? q.OrderBy(f => f.Name) : q.OrderByDescending(f => f.Name),
            "size" => query.Ascending ? q.OrderBy(f => f.Size) : q.OrderByDescending(f => f.Size),
            "downloads" => query.Ascending ? q.OrderBy(f => f.DownloadCount) : q.OrderByDescending(f => f.DownloadCount),
            "updatedAt" => query.Ascending ? q.OrderBy(f => f.UpdatedAt) : q.OrderByDescending(f => f.UpdatedAt),
            "deletedAt" => query.Ascending ? q.OrderBy(f => f.DeletedAt) : q.OrderByDescending(f => f.DeletedAt),
            _ => query.Ascending ? q.OrderBy(f => f.CreatedAt) : q.OrderByDescending(f => f.CreatedAt),
        };

        var total = await q.LongCountAsync(ct);
        var items = await q
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return PagedResult<StoredFile>.Create(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<StoredFile>> ListRecentByOwnerAsync(Guid ownerId, int take, CancellationToken ct = default) =>
        await Files.AsNoTracking()
            .Where(f => f.OwnerId == ownerId && f.DeletedAt == null)
            .OrderByDescending(f => f.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<long> SumActiveBytesAsync(Guid ownerId, CancellationToken ct = default) =>
        await Files.Where(f => f.OwnerId == ownerId && f.DeletedAt == null)
            .SumAsync(f => (long?)f.Size, ct) ?? 0L;

    public Task<long> CountByOwnerAsync(Guid ownerId, bool includeDeleted, CancellationToken ct = default) =>
        includeDeleted
            ? Files.LongCountAsync(f => f.OwnerId == ownerId, ct)
            : Files.LongCountAsync(f => f.OwnerId == ownerId && f.DeletedAt == null, ct);

    public Task<long> CountSharesByOwnerAsync(Guid ownerId, CancellationToken ct = default) =>
        db.Shares.LongCountAsync(s => s.CreatedBy == ownerId, ct);

    public Task<long> CountAllFilesAsync(CancellationToken ct = default) =>
        Files.LongCountAsync(f => f.DeletedAt == null, ct);

    public async Task<long> SumAllActiveBytesAsync(CancellationToken ct = default) =>
        await Files.Where(f => f.DeletedAt == null).SumAsync(f => (long?)f.Size, ct) ?? 0L;

    public Task<long> CountAllSharesAsync(CancellationToken ct = default) =>
        db.Shares.LongCountAsync(s => s.IsActive, ct);

    public Task<long> CountAllUsersAsync(CancellationToken ct = default) =>
        db.Users.LongCountAsync(ct);

    public Task<int> RemoveAllByOwnerAsync(Guid ownerId, CancellationToken ct = default) =>
        Files.Where(f => f.OwnerId == ownerId).ExecuteDeleteAsync(ct);

    public void Add(StoredFile file) => Files.Add(file);

    public void Update(StoredFile file) => Files.Update(file);

    public void Remove(StoredFile file) => Files.Remove(file);
}

public class FolderRepository(AppDbContext db) : IFolderRepository
{
    public Task<Folder?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Folders.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<IReadOnlyList<Folder>> ListByOwnerAsync(Guid ownerId, CancellationToken ct = default) =>
        await db.Folders.AsNoTracking()
            .Where(f => f.OwnerId == ownerId)
            .ToListAsync(ct);

    public Task<Folder?> FindByNameAndParentAsync(Guid ownerId, Guid? parentId, string name, CancellationToken ct = default) =>
        db.Folders.AsNoTracking()
            .FirstOrDefaultAsync(f => f.OwnerId == ownerId && f.ParentId == parentId && f.Name == name, ct);

    public void Add(Folder folder) => db.Folders.Add(folder);

    public void Update(Folder folder) => db.Folders.Update(folder);

    public void Remove(Folder folder) => db.Folders.Remove(folder);
}

public class ShareRepository(AppDbContext db) : IShareRepository
{
    public Task<ShareLink?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Shares.Include(s => s.File).ThenInclude(f => f!.Owner)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<ShareLink?> GetByTokenWithFileAsync(string token, CancellationToken ct = default) =>
        db.Shares.Include(s => s.File)
            .FirstOrDefaultAsync(s => s.Token == token, ct);

    public async Task<PagedResult<ShareLink>> ListByOwnerAsync(Guid ownerId, int page, int pageSize,
        CancellationToken ct = default)
    {
        var q = db.Shares.Include(s => s.File)
            .Where(s => s.CreatedBy == ownerId);
        var total = await q.LongCountAsync(ct);
        var items = await q.OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);
        return PagedResult<ShareLink>.Create(items, total, page, pageSize);
    }

    public async Task<PagedResult<ShareLink>> ListAllAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.Shares.Include(s => s.File).ThenInclude(f => f!.Owner).AsNoTracking();
        var total = await q.LongCountAsync(ct);
        var items = await q.OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return PagedResult<ShareLink>.Create(items, total, page, pageSize);
    }

    public void Add(ShareLink share) => db.Shares.Add(share);

    public void Update(ShareLink share) => db.Shares.Update(share);

    public void Remove(ShareLink share) => db.Shares.Remove(share);
}

public class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tokens = await db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            token.RevokedReason = reason;
        }
    }

    public Task<int> PurgeExpiredAsync(DateTime utcNow, CancellationToken ct = default) =>
        db.RefreshTokens.Where(rt => rt.ExpiresAt < utcNow).ExecuteDeleteAsync(ct);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);

    public void Update(RefreshToken token) => db.RefreshTokens.Update(token);
}

public class ActivityRepository(AppDbContext db) : IActivityRepository
{
    public async Task<PagedResult<ActivityLog>> ListByUserAsync(Guid userId, int page, int pageSize,
        CancellationToken ct = default)
    {
        var q = db.ActivityLogs.AsNoTracking().Where(a => a.UserId == userId);
        var total = await q.LongCountAsync(ct);
        var items = await q.OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return PagedResult<ActivityLog>.Create(items, total, page, pageSize);
    }

    public async Task<PagedResult<ActivityLog>> ListAllAsync(ActivityAction? action, Guid? userId,
        int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.ActivityLogs.AsNoTracking().AsQueryable();
        if (action.HasValue)
        {
            q = q.Where(a => a.Action == action.Value);
        }

        if (userId.HasValue)
        {
            q = q.Where(a => a.UserId == userId.Value);
        }

        var total = await q.LongCountAsync(ct);
        var items = await q.OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return PagedResult<ActivityLog>.Create(items, total, page, pageSize);
    }

    public Task AddAsync(ActivityLog entry, CancellationToken ct = default)
    {
        db.ActivityLogs.Add(entry);
        return Task.CompletedTask;
    }
}

public class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
