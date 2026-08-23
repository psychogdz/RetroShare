using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetroShare.API.Extensions;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Domain.Constants;

namespace RetroShare.API.Controllers;

/// <summary>The recycle bin: list trashed files, restore them, or destroy them permanently.</summary>
[ApiController]
[Route("api/trash")]
public sealed class TrashController(
    IFileService files,
    IPermissionChecker permissionChecker) : ControllerBase
{
    /// <summary>Lists the caller's trashed files.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.FilesView)]
    [ProducesResponseType(typeof(PagedResult<FileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await files.ListAsync(User.GetUserId(), null, search, null,
            "deletedAt", ascending: false, page, pageSize, trashOnly: true, ct));
    }

    /// <summary>Restores a trashed file.</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = Permissions.FilesRestore)]
    [ProducesResponseType(typeof(FileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        var isAdmin = await permissionChecker.HasPermissionAsync(User, Permissions.SystemManage, ct);
        return Ok(await files.RestoreAsync(id, User.GetUserId(), isAdmin, ct));
    }

    /// <summary>Permanently deletes a trashed file and its physical blob.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.FilesDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var isAdmin = await permissionChecker.HasPermissionAsync(User, Permissions.SystemManage, ct);
        await files.DeletePermanentlyAsync(id, User.GetUserId(), isAdmin, HttpContext.TryGetRemoteIp(), ct);
        return NoContent();
    }
}
