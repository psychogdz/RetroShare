using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetroShare.API.Extensions;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Domain.Constants;

namespace RetroShare.API.Controllers;

/// <summary>Folder tree management: create, rename, move, delete and browse contents.</summary>
[ApiController]
[Route("api/folders")]
public sealed class FoldersController(IFolderService folders) : ControllerBase
{
    /// <summary>Flat list of all the caller's folders (for move dialogs and trees).</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.FoldersView)]
    [ProducesResponseType(typeof(IReadOnlyList<FolderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await folders.ListAllAsync(User.GetUserId(), ct));

    /// <summary>Contents of a folder (or the root when folderId is omitted): breadcrumbs,
    /// subfolders and a page of files.</summary>
    [HttpGet("contents")]
    [Authorize(Policy = Permissions.FoldersView)]
    [ProducesResponseType(typeof(FolderContentsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Contents(
        [FromQuery] Guid? folderId,
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] string sort = "createdAt",
        [FromQuery] bool ascending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await folders.GetContentsAsync(User.GetUserId(), folderId, search, type,
            sort, ascending, page, pageSize, ct));
    }

    /// <summary>Creates a folder under a parent (root when parentId is omitted).</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.FoldersCreate)]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest request, CancellationToken ct)
    {
        var folder = await folders.CreateAsync(User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(List), new { id = folder.Id }, folder);
    }

    /// <summary>Renames a folder.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.FoldersRename)]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameFolderRequest request, CancellationToken ct) =>
        Ok(await folders.RenameAsync(id, User.GetUserId(), request.Name, ct));

    /// <summary>Moves a folder under another parent (root when parentId is omitted).</summary>
    [HttpPost("{id:guid}/move")]
    [Authorize(Policy = Permissions.FoldersRename)]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveFolderRequest request, CancellationToken ct) =>
        Ok(await folders.MoveAsync(id, User.GetUserId(), request.ParentId, ct));

    /// <summary>Deletes a folder subtree; contained files move to trash.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.FoldersDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await folders.DeleteAsync(id, User.GetUserId(), HttpContext.TryGetRemoteIp(), ct);
        return NoContent();
    }
}
