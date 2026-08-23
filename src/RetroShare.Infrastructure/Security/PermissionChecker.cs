using System.Collections.ObjectModel;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RetroShare.Application.Interfaces;
using RetroShare.Infrastructure.Data;

namespace RetroShare.Infrastructure.Security;

/// <summary>Resolves a user's effective permission set live from the database with a short
/// in-memory cache, so role/permission changes apply without waiting for token renewal.
/// Invalidation bumps a shared key version, orphaning old cache entries.</summary>
public sealed class PermissionChecker(AppDbContext db, IMemoryCache cache) : IPermissionChecker
{
    private static TimeSpan CacheDuration => TimeSpan.FromSeconds(30);
    private static long _keyVersion;

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        var version = Interlocked.Read(ref _keyVersion);
        var permissions = await cache.GetOrCreateAsync($"perms:{version}:{userId:N}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var names = await db.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Join(db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
                .Distinct()
                .Join(db.Permissions, pid => pid, p => p.Id, (pid, p) => p.Name)
                .ToListAsync(ct);
            return names;
        });

        return permissions ?? [];
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal principal, string permission, CancellationToken ct = default)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var userIdClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return false;
        }

        var permissions = await GetPermissionsAsync(userId, ct);
        return permissions.Contains(permission);
    }

    public void Invalidate(Guid userId) => cache.Remove($"perms:{Interlocked.Read(ref _keyVersion)}:{userId:N}");

    public void InvalidateAll() => Interlocked.Increment(ref _keyVersion);
}
