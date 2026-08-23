using Microsoft.Extensions.Logging;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Application.Mapping;
using RetroShare.Application.Validation;
using RetroShare.Domain.Entities;
using RetroShare.Domain.Enums;

namespace RetroShare.Application.Services;

/// <summary>Folder tree management: per-owner hierarchy, sibling name uniqueness, cycle-safe
/// moves and recursive soft delete.</summary>
public sealed class FolderService(
    IFolderRepository folders,
    IFileRepository files,
    IActivityLogger activity,
    IUnitOfWork unitOfWork,
    ILogger<FolderService> logger) : IFolderService
{
    public async Task<FolderDto> CreateAsync(Guid userId, CreateFolderRequest request, CancellationToken ct = default)
    {
        var name = Validators.SanitizeName(request.Name)
            ?? throw new ValidationException("Folder name is empty or contains only invalid characters.");

        var parentId = await ResolveParentAsync(userId, request.ParentId, ct);
        if (await folders.FindByNameAndParentAsync(userId, parentId, name, ct) is not null)
        {
            throw new ConflictException($"A folder named '{name}' already exists there.", "FOLDER_NAME_TAKEN");
        }

        var folder = new Folder { OwnerId = userId, Name = name, ParentId = parentId };
        folders.Add(folder);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.FolderCreated,
            $"Created folder '{name}'.", userId, "Folder", folder.Id.ToString(), null, ct);
        return folder.ToDto();
    }

    public async Task<FolderDto> RenameAsync(Guid folderId, Guid userId, string newName, CancellationToken ct = default)
    {
        var folder = await GetOwnedAsync(folderId, userId, ct);
        var name = Validators.SanitizeName(newName)
            ?? throw new ValidationException("Folder name is empty or contains only invalid characters.");

        var existing = await folders.FindByNameAndParentAsync(userId, folder.ParentId, name, ct);
        if (existing is not null && existing.Id != folder.Id)
        {
            throw new ConflictException($"A folder named '{name}' already exists there.", "FOLDER_NAME_TAKEN");
        }

        folder.Name = name;
        folder.UpdatedAt = DateTime.UtcNow;
        folders.Update(folder);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.FolderRenamed,
            $"Renamed folder to '{name}'.", userId, "Folder", folder.Id.ToString(), null, ct);
        return folder.ToDto();
    }

    public async Task<FolderDto> MoveAsync(Guid folderId, Guid userId, Guid? targetParentId, CancellationToken ct = default)
    {
        var folder = await GetOwnedAsync(folderId, userId, ct);

        if (targetParentId == folderId)
        {
            throw new ValidationException("A folder cannot contain itself.");
        }

        // Walk up from the target parent to ensure we are not creating a cycle.
        if (targetParentId is not null)
        {
            if (await IsDescendantAsync(userId, folderId, targetParentId.Value, ct))
            {
                throw new ValidationException("Cannot move a folder into one of its own subfolders.");
            }
        }

        var parentId = await ResolveParentAsync(userId, targetParentId, ct);
        var existing = await folders.FindByNameAndParentAsync(userId, parentId, folder.Name, ct);
        if (existing is not null && existing.Id != folder.Id)
        {
            throw new ConflictException($"A folder named '{folder.Name}' already exists there.", "FOLDER_NAME_TAKEN");
        }

        folder.ParentId = parentId;
        folder.UpdatedAt = DateTime.UtcNow;
        folders.Update(folder);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.FolderRenamed,
            $"Moved folder '{folder.Name}'.", userId, "Folder", folder.Id.ToString(), null, ct);
        return folder.ToDto();
    }

    public async Task DeleteAsync(Guid folderId, Guid userId, string? ipAddress = null, CancellationToken ct = default)
    {
        // Work exclusively with the no-tracking snapshot so attaching nodes as Modified
        // cannot collide with a differently-tracked instance of the same folder.
        var allFolders = await folders.ListByOwnerAsync(userId, ct);
        var folder = allFolders.FirstOrDefault(f => f.Id == folderId && !f.IsDeleted)
            ?? throw new NotFoundException("Folder not found.");

        // Collect the subtree rooted at the folder (in-memory tree walk; folder sets are small).
        var toDelete = new HashSet<Guid> { folderId };
        var frontier = new Queue<Guid>();
        frontier.Enqueue(folderId);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var child in allFolders.Where(f => f.ParentId == current && !f.IsDeleted))
            {
                toDelete.Add(child.Id);
                frontier.Enqueue(child.Id);
            }
        }

        var now = DateTime.UtcNow;
        foreach (var doomed in allFolders.Where(f => toDelete.Contains(f.Id)))
        {
            doomed.DeletedAt = now;
            doomed.UpdatedAt = now;
            folders.Update(doomed);
        }

        // Trash files living anywhere in the deleted subtree. Detach the Owner navigation so
        // attaching the file does not drag a duplicate User instance into the change tracker.
        var trashed = await files.SearchAsync(new FileListQuery(
            OwnerId: userId, Page: 1, PageSize: int.MaxValue), ct);
        foreach (var file in trashed.Items.Where(f => f.FolderId is not null && toDelete.Contains(f.FolderId.Value)))
        {
            if (file.DeletedAt is null)
            {
                file.DeletedAt = now;
                file.UpdatedAt = now;
                file.Owner = null!;
                files.Update(file);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        await activity.LogAsync(ActivityAction.FolderDeleted,
            $"Deleted folder '{folder.Name}' ({toDelete.Count - 1} subfolder(s)).",
            userId, "Folder", folder.Id.ToString(), ipAddress, ct);
        logger.LogInformation("Folder {FolderId} deleted ({Count} nodes)", folderId, toDelete.Count);
    }

    public async Task<FolderContentsDto> GetContentsAsync(Guid userId, Guid? folderId, string? search,
        string? typeFilter, string sort, bool ascending, int page, int pageSize, CancellationToken ct = default)
    {
        var allFolders = await folders.ListByOwnerAsync(userId, ct);

        FolderBreadcrumb[] breadcrumbs;
        if (folderId is null)
        {
            breadcrumbs = [];
        }
        else
        {
            var folder = allFolders.FirstOrDefault(f => f.Id == folderId && !f.IsDeleted)
                ?? throw new NotFoundException("Folder not found.");
            breadcrumbs = BuildBreadcrumb(allFolders, folder).ToArray();
        }

        var subfolders = allFolders
            .Where(f => f.ParentId == folderId && !f.IsDeleted)
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.ToDto())
            .ToList();

        // Search across the whole account when a search term is present; otherwise scope to folder.
        var effectiveFolderId = string.IsNullOrWhiteSpace(search) ? folderId : null;
        var filesPage = await files.SearchAsync(new FileListQuery(
            OwnerId: userId, FolderId: effectiveFolderId, Search: search, TypeFilter: typeFilter,
            Sort: sort, Ascending: ascending, Page: page, PageSize: pageSize), ct);

        return new FolderContentsDto
        {
            Breadcrumbs = breadcrumbs,
            Folders = subfolders,
            Files = PagedResult<FileDto>.Create(
                filesPage.Items.Select(f => f.ToDto()).ToList(),
                filesPage.Total, filesPage.Page, filesPage.PageSize),
        };
    }

    public async Task<IReadOnlyList<FolderDto>> ListAllAsync(Guid userId, CancellationToken ct = default)
    {
        var all = await folders.ListByOwnerAsync(userId, ct);
        return all.Where(f => !f.IsDeleted)
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.ToDto())
            .ToList();
    }

    private async Task<Folder> GetOwnedAsync(Guid folderId, Guid userId, CancellationToken ct)
    {
        var folder = await folders.GetByIdAsync(folderId, ct);
        if (folder is null || folder.IsDeleted || folder.OwnerId != userId)
        {
            throw new NotFoundException("Folder not found.");
        }

        return folder;
    }

    private async Task<Guid?> ResolveParentAsync(Guid userId, Guid? parentId, CancellationToken ct)
    {
        if (parentId is null)
        {
            return null;
        }

        var parent = await folders.GetByIdAsync(parentId.Value, ct);
        if (parent is null || parent.IsDeleted || parent.OwnerId != userId)
        {
            throw new NotFoundException("Parent folder not found.");
        }

        return parent.Id;
    }

    private async Task<bool> IsDescendantAsync(Guid userId, Guid ancestorId, Guid candidateId, CancellationToken ct)
    {
        // True when candidateId sits anywhere inside the subtree of ancestorId.
        var all = await folders.ListByOwnerAsync(userId, ct);
        var byParent = all
            .Where(f => f.ParentId.HasValue)
            .GroupBy(f => f.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Id).ToList());

        var stack = new Stack<Guid>();
        stack.Push(ancestorId);
        var seen = new HashSet<Guid>();
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            if (current == candidateId)
            {
                return true;
            }

            if (byParent.TryGetValue(current, out var children))
            {
                foreach (var child in children)
                {
                    stack.Push(child);
                }
            }
        }

        return false;
    }

    private static IEnumerable<FolderBreadcrumb> BuildBreadcrumb(IReadOnlyList<Folder> all, Folder leaf)
    {
        var byId = all.ToDictionary(f => f.Id);
        var chain = new List<FolderBreadcrumb>();
        var current = leaf;
        while (current is not null)
        {
            chain.Add(new FolderBreadcrumb { Id = current.Id, Name = current.Name });
            current = current.ParentId is { } pid && byId.TryGetValue(pid, out var parent)
                ? parent
                : null;
        }

        chain.Reverse();
        return chain;
    }
}
