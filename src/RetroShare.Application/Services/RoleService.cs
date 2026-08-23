using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Application.Mapping;
using RetroShare.Domain.Constants;
using RetroShare.Domain.Entities;
using RetroShare.Domain.Enums;

namespace RetroShare.Application.Services;

/// <summary>Role and permission-catalog management. Roles are pure permission groups;
/// nothing here special-cases specific role names except protecting the seeded admin role
/// from being stripped of its recovery permission.</summary>
public sealed class RoleService(
    IRoleRepository roles,
    IPermissionRepository permissions,
    IPermissionChecker permissionChecker,
    IActivityLogger activity,
    IUnitOfWork unitOfWork) : IRoleService
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default)
    {
        var all = await roles.GetAllWithUsersAsync(ct);
        return all.Select(r => r.ToDto(r.Users.Count)).ToList();
    }

    public async Task<RoleDto> GetAsync(int id, CancellationToken ct = default)
    {
        var role = await roles.GetByIdWithPermissionsAsync(id, ct) ?? throw new NotFoundException("Role not found.");
        var userCount = await roles.CountUsersAsync(id, ct);
        return role.ToDto(userCount);
    }

    public async Task<RoleDto> CreateAsync(CreateRoleRequest request, Guid actorId, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (await roles.GetByNameAsync(name, ct) is not null)
        {
            throw new ConflictException($"A role named '{name}' already exists.", "ROLE_NAME_TAKEN");
        }

        var permissionList = await ResolvePermissionsAsync(request.PermissionIds, ct);
        var role = new Role
        {
            Name = name,
            Description = request.Description?.Trim() ?? string.Empty,
            IsSystem = false,
            Permissions = permissionList
                .Select(p => new RolePermission { PermissionId = p.Id })
                .ToList(),
        };

        roles.Add(role);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.RoleCreated,
            $"Created role '{role.Name}' with {permissionList.Count} permission(s).",
            actorId, "Role", role.Id.ToString(), null, ct);
        return await GetAsync(role.Id, ct);
    }

    public async Task<RoleDto> UpdateAsync(int id, UpdateRoleRequest request, Guid actorId, CancellationToken ct = default)
    {
        var role = await roles.GetByIdWithPermissionsAsync(id, ct) ?? throw new NotFoundException("Role not found.");

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (role.IsSystem && !string.Equals(role.Name, name, StringComparison.Ordinal))
            {
                throw new ValidationException("System roles cannot be renamed.");
            }

            var existing = await roles.GetByNameAsync(name, ct);
            if (existing is not null && existing.Id != role.Id)
            {
                throw new ConflictException($"A role named '{name}' already exists.", "ROLE_NAME_TAKEN");
            }

            role.Name = name;
        }

        if (request.Description is not null)
        {
            role.Description = request.Description.Trim();
        }

        if (request.PermissionIds is not null)
        {
            var permissionList = await ResolvePermissionsAsync(request.PermissionIds, ct);

            // One of the few deliberate system-level guards: the seeded admin role must keep
            // its recovery permission, otherwise a misconfiguration could lock out every
            // administrator permanently.
            if (role.IsSystem && await IsSeededAdminRoleAsync(role, ct)
                && permissionList.All(p => p.Name != Permissions.SystemManage))
            {
                throw new ValidationException("The admin role must keep the 'system.manage' permission.");
            }

            role.Permissions.Clear();
            foreach (var permission in permissionList)
            {
                role.Permissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }

            await activity.LogAsync(ActivityAction.RoleUpdated,
                $"Set role '{role.Name}' to {permissionList.Count} permission(s).",
                actorId, "Role", role.Id.ToString(), null, ct);
        }

        roles.Update(role);
        await unitOfWork.SaveChangesAsync(ct);
        if (request.PermissionIds is not null)
        {
            // The role's permission set changed — every holder's cache must go.
            permissionChecker.InvalidateAll();
        }

        return await GetAsync(role.Id, ct);
    }

    public async Task DeleteAsync(int id, Guid actorId, CancellationToken ct = default)
    {
        var role = await roles.GetByIdWithPermissionsAsync(id, ct) ?? throw new NotFoundException("Role not found.");

        if (role.IsSystem)
        {
            throw new ValidationException("System roles cannot be deleted.");
        }

        var assigned = await roles.CountUsersAsync(id, ct);
        if (assigned > 0)
        {
            throw new ConflictException(
                $"The role is still assigned to {assigned} user(s). Reassign them first.", "ROLE_IN_USE");
        }

        var name = role.Name;
        roles.Remove(role);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.RoleDeleted,
            $"Deleted role '{name}'.", actorId, "Role", id.ToString(), null, ct);
    }

    private async Task<bool> IsSeededAdminRoleAsync(Role role, CancellationToken ct)
    {
        var admin = await roles.GetByNameAsync(RoleNames.Admin, ct);
        return admin is not null && admin.Id == role.Id;
    }

    private async Task<List<Permission>> ResolvePermissionsAsync(IReadOnlyList<int> permissionIds, CancellationToken ct)
    {
        if (permissionIds.Count == 0)
        {
            return [];
        }

        var distinct = permissionIds.Distinct().ToList();
        var all = await permissions.GetAllAsync(ct);
        var found = all.Where(p => distinct.Contains(p.Id)).ToList();

        if (found.Count != distinct.Count)
        {
            throw new ValidationException("One or more permission ids are unknown.");
        }

        return found;
    }
}

public sealed class PermissionService(IPermissionRepository permissions) : IPermissionService
{
    public async Task<IReadOnlyList<PermissionDto>> ListAsync(CancellationToken ct = default)
    {
        var all = await permissions.GetAllAsync(ct);
        return all
            .OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.ToDto())
            .ToList();
    }
}
