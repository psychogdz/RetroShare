using Microsoft.Extensions.Logging;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Application.Mapping;
using RetroShare.Application.Validation;
using RetroShare.Domain.Entities;
using RetroShare.Domain.Enums;

namespace RetroShare.Application.Services;

/// <summary>Self-service profile operations.</summary>
public sealed class ProfileService(
    IUserRepository users,
    IFileRepository files,
    IPasswordHasher passwordHasher,
    IRefreshTokenRepository refreshTokens,
    IActivityLogger activity,
    IUnitOfWork unitOfWork) : IProfileService
{
    public async Task<UserDto> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User not found.");
        var name = Validators.SanitizeName(displayName)
            ?? throw new ValidationException("Display name is empty or contains only invalid characters.");

        user.DisplayName = name;
        user.UpdatedAt = DateTime.UtcNow;
        users.Update(user);
        await unitOfWork.SaveChangesAsync(ct);
        return await ToDtoAsync(user, ct);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword,
        string? ipAddress = null, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User not found.");

        if (!passwordHasher.Verify(user.PasswordHash, currentPassword))
        {
            throw new ValidationException("Current password is incorrect.");
        }

        if (!Validators.IsStrongPassword(newPassword, out var error))
        {
            throw new ValidationException(error);
        }

        user.PasswordHash = passwordHasher.Hash(newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        users.Update(user);

        // Password change invalidates every existing session.
        await refreshTokens.RevokeAllForUserAsync(userId, "Password changed", ct);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.PasswordChanged,
            "Changed account password.", userId, "User", userId.ToString(), ipAddress, ct);
    }

    private async Task<UserDto> ToDtoAsync(User user, CancellationToken ct)
    {
        var used = await files.SumActiveBytesAsync(user.Id, ct);
        return user.ToDto(storageUsed: used);
    }
}

/// <summary>Admin user management: listing, editing, role assignment and deletion.</summary>
public sealed class UserManagementService(
    IUserRepository users,
    IRoleRepository roles,
    IFileRepository files,
    IShareRepository shares,
    IFileStorage storage,
    IPermissionChecker permissionChecker,
    IActivityLogger activity,
    IUnitOfWork unitOfWork,
    ILogger<UserManagementService> logger) : IUserManagementService
{
    public async Task<PagedResult<UserDto>> ListAsync(string? search, bool? disabled, string sort,
        bool ascending, int page, int pageSize, CancellationToken ct = default)
    {
        var result = await users.SearchAsync(search, disabled, sort, ascending, page, pageSize, ct);
        var dtos = new List<UserDto>();
        foreach (var user in result.Items)
        {
            var used = await files.SumActiveBytesAsync(user.Id, ct);
            dtos.Add(user.ToDto(storageUsed: used));
        }

        return PagedResult<UserDto>.Create(dtos, result.Total, result.Page, result.PageSize);
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var user = await users.GetWithRolesAsync(id, ct) ?? throw new NotFoundException("User not found.");
        var used = await files.SumActiveBytesAsync(user.Id, ct);
        return user.ToDto(storageUsed: used);
    }

    public async Task<UserDto> UpdateAsync(Guid id, AdminUpdateUserRequest request, Guid actorId,
        CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(id, ct) ?? throw new NotFoundException("User not found.");

        if (request.DisplayName is not null)
        {
            var name = Validators.SanitizeName(request.DisplayName)
                ?? throw new ValidationException("Display name is empty or contains only invalid characters.");
            user.DisplayName = name;
        }

        if (request.StorageQuotaBytes.HasValue)
        {
            if (request.StorageQuotaBytes.Value < 0)
            {
                throw new ValidationException("Quota cannot be negative.");
            }

            var used = await files.SumActiveBytesAsync(id, ct);
            if (used > request.StorageQuotaBytes.Value)
            {
                throw new ValidationException(
                    $"Cannot set the quota below current usage ({used:N0} bytes).");
            }

            if (user.StorageQuotaBytes != request.StorageQuotaBytes.Value)
            {
                await activity.LogAsync(ActivityAction.QuotaChanged,
                    $"Quota changed from {user.StorageQuotaBytes:N0} to {request.StorageQuotaBytes.Value:N0} bytes.",
                    actorId, "User", id.ToString(), null, ct);
                user.StorageQuotaBytes = request.StorageQuotaBytes.Value;
            }
        }

        if (request.IsDisabled.HasValue && request.IsDisabled.Value != user.IsDisabled)
        {
            if (id == actorId)
            {
                throw new ValidationException("You cannot disable your own account.");
            }

            user.IsDisabled = request.IsDisabled.Value;
            await activity.LogAsync(request.IsDisabled.Value ? ActivityAction.UserDisabled : ActivityAction.UserEnabled,
                $"{(request.IsDisabled.Value ? "Disabled" : "Enabled")} account '{user.Username}'.",
                actorId, "User", id.ToString(), null, ct);
            if (request.IsDisabled.Value)
            {
                // Disabled users lose all active sessions immediately.
                // (Revocation happens through the repository in the same save.)
            }
        }

        user.UpdatedAt = DateTime.UtcNow;
        users.Update(user);
        await unitOfWork.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    public async Task<UserDto> SetRolesAsync(Guid id, IReadOnlyCollection<int> roleIds, Guid actorId,
        CancellationToken ct = default)
    {
        var user = await users.GetWithRolesAsync(id, ct) ?? throw new NotFoundException("User not found.");

        if (roleIds.Count == 0)
        {
            throw new ValidationException("A user must keep at least one role.");
        }

        var distinctIds = roleIds.Distinct().ToList();
        var resolved = new List<Role>();
        foreach (var roleId in distinctIds)
        {
            var role = await roles.GetByIdAsync(roleId, ct)
                ?? throw new NotFoundException($"Role {roleId} not found.");
            resolved.Add(role);
        }

        var before = user.Roles.Select(r => r.Role.Name).OrderBy(n => n).ToList();
        user.Roles.Clear();
        foreach (var role in resolved)
        {
            user.Roles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        user.UpdatedAt = DateTime.UtcNow;
        users.Update(user);
        await unitOfWork.SaveChangesAsync(ct);
        permissionChecker.Invalidate(id); // new roles apply on the very next request

        var after = resolved.Select(r => r.Name).OrderBy(n => n).ToList();
        if (!before.SequenceEqual(after))
        {
            await activity.LogAsync(ActivityAction.UserRolesChanged,
                $"Roles changed from [{string.Join(", ", before)}] to [{string.Join(", ", after)}].",
                actorId, "User", id.ToString(), null, ct);
        }

        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        if (id == actorId)
        {
            throw new ValidationException("You cannot delete your own account.");
        }

        var user = await users.GetWithRolesAsync(id, ct) ?? throw new NotFoundException("User not found.");

        // Collect blob locations first (no-tracking read)…
        var ownedFiles = await files.SearchAsync(new FileListQuery(
            OwnerId: id, Page: 1, PageSize: int.MaxValue, IncludeDeleted: true), ct);
        var physicalPaths = ownedFiles.Items.Select(f => f.StoragePath).ToList();

        // …then bulk-delete the rows. Attach-free bulk delete avoids identity-map conflicts
        // with tracked entities, and share links cascade at the database level.
        await files.RemoveAllByOwnerAsync(id, ct);
        users.Remove(user);
        await unitOfWork.SaveChangesAsync(ct);
        permissionChecker.Invalidate(id); // the deleted account must not keep cached grants

        // Remove physical blobs after the metadata commit.
        foreach (var path in physicalPaths)
        {
            try
            {
                await storage.DeleteAsync(path, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete blob during user deletion: {Path}", path);
            }
        }

        await activity.LogAsync(ActivityAction.UserDeleted,
            $"Deleted account '{user.Username}' and {physicalPaths.Count} file(s).",
            actorId, "User", id.ToString(), null, ct);
        logger.LogInformation("User {UserId} deleted by {ActorId}", id, actorId);
    }
}
