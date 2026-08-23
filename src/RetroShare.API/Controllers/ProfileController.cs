using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetroShare.API.Extensions;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Domain.Constants;

namespace RetroShare.API.Controllers;

/// <summary>Self-service profile: display name and password.</summary>
[ApiController]
[Route("api/profile")]
public sealed class ProfileController(
    IProfileService profile,
    IAuthService auth) : ControllerBase
{
    /// <summary>The caller's profile, roles, permissions and storage usage.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.ProfileView)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(await auth.GetMeAsync(User.GetUserId(), ct));

    /// <summary>Updates the caller's display name.</summary>
    [HttpPut]
    [Authorize(Policy = Permissions.ProfileUpdate)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update([FromBody] UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await profile.UpdateDisplayNameAsync(User.GetUserId(), request.DisplayName, ct));

    /// <summary>Changes the caller's password and revokes all sessions.</summary>
    [HttpPost("password")]
    [Authorize(Policy = Permissions.ProfileUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        await profile.ChangePasswordAsync(User.GetUserId(), request.CurrentPassword, request.NewPassword,
            HttpContext.TryGetRemoteIp(), ct);
        return NoContent();
    }
}

/// <summary>Personal dashboard statistics.</summary>
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboard) : ControllerBase
{
    /// <summary>Storage, file, share and activity summary for the caller.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.ProfileView)]
    [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(CancellationToken ct) =>
        Ok(await dashboard.GetUserDashboardAsync(User.GetUserId(), ct));

    /// <summary>System-wide statistics (admin).</summary>
    [HttpGet("admin")]
    [Authorize(Policy = Permissions.SystemManage)]
    [ProducesResponseType(typeof(AdminDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Admin(CancellationToken ct) =>
        Ok(await dashboard.GetAdminDashboardAsync(ct));
}

/// <summary>Activity history: personal always, system-wide with system.manage.</summary>
[ApiController]
[Route("api/activity")]
public sealed class ActivityController(IActivityService activity) : ControllerBase
{
    /// <summary>The caller's own audit trail.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.ProfileView)]
    [ProducesResponseType(typeof(PagedResult<ActivityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await activity.ListOwnAsync(User.GetUserId(), page, pageSize, ct));
    }

    /// <summary>System-wide audit trail with optional filters.</summary>
    [HttpGet("all")]
    [Authorize(Policy = Permissions.SystemManage)]
    [ProducesResponseType(typeof(PagedResult<ActivityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> All(
        [FromQuery] string? action,
        [FromQuery] Guid? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        Domain.Enums.ActivityAction? parsed =
            Enum.TryParse<Domain.Enums.ActivityAction>(action, out var actionValue) ? actionValue : null;
        return Ok(await activity.ListAllAsync(parsed, userId, page, pageSize, ct));
    }
}
