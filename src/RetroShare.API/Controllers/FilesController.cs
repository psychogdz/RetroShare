using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetroShare.API.Extensions;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Domain.Constants;

namespace RetroShare.API.Controllers;

/// <summary>File metadata operations. The bytes themselves stream over the gRPC data plane
/// (FileTransfer.Upload / FileTransfer.Download) — there is intentionally no REST upload body.</summary>
[ApiController]
[Route("api/files")]
public sealed class FilesController(
    IFileService files,
    IPermissionChecker permissionChecker) : ControllerBase
{
    private Task<bool> IsAdminAsync(CancellationToken ct) =>
        permissionChecker.HasPermissionAsync(User, Permissions.SystemManage, ct);

    /// <summary>Lists the caller's files; supports folder scoping, search, type filter,
    /// sorting and paging. Set trash=true to list trashed files.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.FilesView)]
    [ProducesResponseType(typeof(PagedResult<FileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? folderId,
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] string sort = "createdAt",
        [FromQuery] bool ascending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool trash = false,
        CancellationToken ct = default)
    {
        Normalize(ref page, ref pageSize);
        var result = await files.ListAsync(User.GetUserId(), folderId, search, type,
            sort, ascending, page, pageSize, trash, ct);
        return Ok(result);
    }

    /// <summary>File metadata for a single file (owner or admin).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.FilesView)]
    [ProducesResponseType(typeof(FileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(await files.GetAsync(id, User.GetUserId(), await IsAdminAsync(ct), ct));

    /// <summary>Renames a file (display name; the stored blob keeps its internal name).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.FilesRename)]
    [ProducesResponseType(typeof(FileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameFileRequest request, CancellationToken ct) =>
        Ok(await files.RenameAsync(id, User.GetUserId(), await IsAdminAsync(ct), request.Name, ct));

    /// <summary>Moves a file into another folder (null = root).</summary>
    [HttpPost("{id:guid}/move")]
    [Authorize(Policy = Permissions.FilesView)]
    [ProducesResponseType(typeof(FileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveFileRequest request, CancellationToken ct) =>
        Ok(await files.MoveAsync(id, User.GetUserId(), await IsAdminAsync(ct), request.FolderId, ct));

    /// <summary>Soft-deletes a file (moves it to trash). Use ?permanent=true for hard delete.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.FilesDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool permanent, CancellationToken ct)
    {
        if (permanent)
        {
            await files.DeletePermanentlyAsync(id, User.GetUserId(), await IsAdminAsync(ct),
                HttpContext.TryGetRemoteIp(), ct);
        }
        else
        {
            await files.DeleteAsync(id, User.GetUserId(), await IsAdminAsync(ct),
                HttpContext.TryGetRemoteIp(), ct);
        }

        return NoContent();
    }

    /// <summary>Restores a trashed file.</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = Permissions.FilesRestore)]
    [ProducesResponseType(typeof(FileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct) =>
        Ok(await files.RestoreAsync(id, User.GetUserId(), await IsAdminAsync(ct), ct));

    /// <summary>Admin listing across all users.</summary>
    [HttpGet("all")]
    [Authorize(Policy = Permissions.UsersView)]
    [ProducesResponseType(typeof(PagedResult<FileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAll(
        [FromQuery] string? search,
        [FromQuery] Guid? ownerId,
        [FromQuery] string sort = "createdAt",
        [FromQuery] bool ascending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeDeleted = false,
        CancellationToken ct = default)
    {
        Normalize(ref page, ref pageSize);
        var result = await files.ListAllAsync(search, ownerId, sort, ascending, page, pageSize,
            includeDeleted, ct);
        return Ok(result);
    }

    private static void Normalize(ref int page, ref int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
    }
}
