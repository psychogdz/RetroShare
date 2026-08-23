using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetroShare.API.Extensions;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Domain.Constants;

namespace RetroShare.API.Controllers;

/// <summary>Share-link lifecycle. Creating a link targets a file; consuming a link is
/// anonymous and streams bytes through the gRPC data plane using the share token.</summary>
[ApiController]
[Route("api/shares")]
public sealed class SharesController(
    IShareService shares,
    IPermissionChecker permissionChecker) : ControllerBase
{
    /// <summary>Creates a share link for a file owned by the caller.</summary>
    [HttpPost("/api/files/{fileId:guid}/share")]
    [Authorize(Policy = Permissions.SharesCreate)]
    [ProducesResponseType(typeof(ShareDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid fileId, [FromBody] CreateShareRequest request, CancellationToken ct)
    {
        request.FileId = fileId;
        var share = await shares.CreateAsync(User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(GetPublicInfo), new { token = share.Token }, share);
    }

    /// <summary>Lists the caller's own share links.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.SharesView)]
    [ProducesResponseType(typeof(PagedResult<ShareDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListOwn(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await shares.ListOwnAsync(User.GetUserId(), page, pageSize, ct));
    }

    /// <summary>Admin listing of all share links.</summary>
    [HttpGet("all")]
    [Authorize(Policy = Permissions.UsersView)]
    [ProducesResponseType(typeof(PagedResult<ShareDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await shares.ListAllAsync(page, pageSize, ct));
    }

    /// <summary>Public metadata for a share link — anonymous, reveals no owner identity.</summary>
    [HttpGet("{token}", Name = nameof(GetPublicInfo))]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicShareInfoDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublicInfo(string token, CancellationToken ct) =>
        Ok(await shares.GetPublicInfoAsync(token, ct));

    /// <summary>Revokes (deactivates) a share link. Owner or admin.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.SharesDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var isAdmin = await permissionChecker.HasPermissionAsync(User, Permissions.SystemManage, ct);
        await shares.RevokeAsync(id, User.GetUserId(), isAdmin, HttpContext.TryGetRemoteIp(), ct);
        return NoContent();
    }
}
