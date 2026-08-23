using RetroShare.Application.DTOs;
using RetroShare.Domain.Enums;

using RetroShare.Application.Common;

namespace RetroShare.Application.Interfaces;

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);

    Task<RoleDto> GetAsync(int id, CancellationToken ct = default);

    Task<RoleDto> CreateAsync(CreateRoleRequest request, Guid actorId, CancellationToken ct = default);

    Task<RoleDto> UpdateAsync(int id, UpdateRoleRequest request, Guid actorId, CancellationToken ct = default);

    /// <summary>Deletes a role. System roles and roles still assigned to users are rejected.</summary>
    Task DeleteAsync(int id, Guid actorId, CancellationToken ct = default);
}

public interface IPermissionService
{
    Task<IReadOnlyList<PermissionDto>> ListAsync(CancellationToken ct = default);
}

public interface IDashboardService
{
    Task<DashboardDto> GetUserDashboardAsync(Guid userId, CancellationToken ct = default);

    Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken ct = default);
}

public interface IActivityService
{
    Task<PagedResult<ActivityDto>> ListOwnAsync(Guid userId, int page, int pageSize, CancellationToken ct = default);

    Task<PagedResult<ActivityDto>> ListAllAsync(ActivityAction? action, Guid? userId,
        int page, int pageSize, CancellationToken ct = default);
}
