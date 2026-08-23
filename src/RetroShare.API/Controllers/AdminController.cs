using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetroShare.API.Extensions;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Domain.Constants;

namespace RetroShare.API.Controllers;

/// <summary>Admin user management. Every action is permission-gated — including this
/// controller's existence in the UI, which the frontend derives from /api/auth/me.</summary>
[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserManagementService users) : ControllerBase
{
    /// <summary>Searches and lists accounts.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.UsersView)]
    [ProducesResponseType(typeof(PagedResult<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool? disabled,
        [FromQuery] string sort = "createdAt",
        [FromQuery] bool ascending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await users.ListAsync(search, disabled, sort, ascending, page, pageSize, ct));
    }

    /// <summary>A single account with roles and storage usage.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.UsersView)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(await users.GetAsync(id, ct));

    /// <summary>Updates display name, enabled state or quota.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.UsersUpdate)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] AdminUpdateUserRequest request, CancellationToken ct) =>
        Ok(await users.UpdateAsync(id, request, User.GetUserId(), ct));

    /// <summary>Replaces the account's role set.</summary>
    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = Permissions.UsersUpdate)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetRoles(Guid id, [FromBody] SetUserRolesRequest request, CancellationToken ct) =>
        Ok(await users.SetRolesAsync(id, request.RoleIds, User.GetUserId(), ct));

    /// <summary>Deletes an account and every file, folder, share and token it owns.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.UsersDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await users.DeleteAsync(id, User.GetUserId(), ct);
        return NoContent();
    }
}

/// <summary>Role and permission-catalog management.</summary>
[ApiController]
[Route("api/roles")]
public sealed class RolesController(IRoleService roles) : ControllerBase
{
    /// <summary>All roles with their permissions and assigned-user counts.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.RolesView)]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await roles.ListAsync(ct));

    /// <summary>A single role.</summary>
    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.RolesView)]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(await roles.GetAsync(id, ct));

    /// <summary>Creates a custom role with a permission set.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.RolesCreate)]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var role = await roles.CreateAsync(request, User.GetUserId(), ct);
        return CreatedAtAction(nameof(Get), new { id = role.Id }, role);
    }

    /// <summary>Updates a role's name, description and (optionally) permission set.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.RolesUpdate)]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRoleRequest request, CancellationToken ct) =>
        Ok(await roles.UpdateAsync(id, request, User.GetUserId(), ct));

    /// <summary>Deletes an unused custom role.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permissions.RolesDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await roles.DeleteAsync(id, User.GetUserId(), ct);
        return NoContent();
    }
}

/// <summary>The read-only permission catalog (permissions are granted via roles).</summary>
[ApiController]
[Route("api/permissions")]
public sealed class PermissionsController(IPermissionService permissions) : ControllerBase
{
    /// <summary>All known permissions grouped by category.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.PermissionsView)]
    [ProducesResponseType(typeof(IReadOnlyList<PermissionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await permissions.ListAsync(ct));
}
