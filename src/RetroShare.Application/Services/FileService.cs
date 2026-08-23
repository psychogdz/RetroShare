using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Application.Mapping;
using RetroShare.Application.Validation;
using RetroShare.Domain.Entities;
using RetroShare.Domain.Enums;

namespace RetroShare.Application.Services;

/// <summary>File metadata management, upload orchestration and download authorization.
/// Enforces ownership checks, quota limits, type validation and trash semantics.</summary>
public sealed class FileService(
    IFileRepository files,
    IFolderRepository folders,
    IUserRepository users,
    IShareRepository shares,
    IFileStorage storage,
    IActivityLogger activity,
    IUnitOfWork unitOfWork,
    IOptions<StorageOptions> storageOptions,
    ILogger<FileService> logger) : IFileService
{
    private readonly StorageOptions _options = storageOptions.Value;

    public async Task<UploadSession> BeginUploadAsync(Guid ownerId, string fileName, long declaredSize,
        string? mimeType, Guid? folderId, CancellationToken ct = default)
    {
        var owner = await users.GetByIdAsync(ownerId, ct)
            ?? throw new NotFoundException("Owner account not found.");
        if (owner.IsDisabled)
        {
            throw new ForbiddenException("This account has been disabled.", "ACCOUNT_DISABLED");
        }

        if (declaredSize < 0)
        {
            throw new ValidationException("Declared file size must not be negative.");
        }

        if (declaredSize > _options.MaxFileSizeBytes)
        {
            throw StorageLimitException.FileTooLarge(_options.MaxFileSizeBytes);
        }

        var sanitized = Validators.SanitizeName(fileName)
            ?? throw new ValidationException("File name is empty or contains only invalid characters.");
        if (Validators.IsReservedName(sanitized))
        {
            throw new ValidationException("That file name is reserved by the operating system.");
        }

        var extension = Path.GetExtension(sanitized).ToLowerInvariant();
        var effectiveMime = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;
        Validators.ValidateFileType(extension, effectiveMime);

        if (folderId.HasValue)
        {
            var folder = await folders.GetByIdAsync(folderId.Value, ct);
            if (folder is null || folder.IsDeleted || folder.OwnerId != ownerId)
            {
                throw new NotFoundException("Target folder not found.");
            }
        }

        // Backend quota enforcement — never trusts client-side math.
        var used = await files.SumActiveBytesAsync(ownerId, ct);
        if (used + declaredSize > owner.StorageQuotaBytes)
        {
            throw StorageLimitException.QuotaExceeded(owner.StorageQuotaBytes);
        }

        var file = new StoredFile
        {
            OwnerId = ownerId,
            Name = sanitized,
            Extension = extension,
            MimeType = effectiveMime,
            Size = declaredSize,
            FolderId = folderId,
            StoredName = $"{Guid.NewGuid():N}{extension}",
        };
        file.StoragePath = storage.BuildRelativePath(ownerId, file.Id);

        var stream = await storage.OpenWriteAsync(file.StoragePath, ct);
        return new UploadSession
        {
            OwnerId = ownerId,
            File = file,
            OutputStream = stream,
            DeclaredSize = declaredSize,
        };
    }

    public async Task<FileDto> CompleteUploadAsync(UploadSession session, string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (session.BytesWritten != session.DeclaredSize)
        {
            await DiscardUploadAsync(session);
            throw new ValidationException(
                $"Uploaded {session.BytesWritten:N0} bytes but the client announced {session.DeclaredSize:N0}. Upload discarded.");
        }

        await session.OutputStream.DisposeAsync();

        var owner = await users.GetByIdAsync(session.OwnerId, ct)
            ?? throw new NotFoundException("Owner account not found.");

        // Re-verify quota against current usage in case of concurrent uploads.
        var used = await files.SumActiveBytesAsync(session.OwnerId, ct);
        if (used + session.File.Size > owner.StorageQuotaBytes)
        {
            await storage.DeleteAsync(session.File.StoragePath, ct);
            throw StorageLimitException.QuotaExceeded(owner.StorageQuotaBytes);
        }

        files.Add(session.File);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.FileUploaded,
            $"Uploaded '{session.File.Name}' ({session.File.Size:N0} bytes).",
            session.OwnerId, "StoredFile", session.File.Id.ToString(), ipAddress, ct);
        logger.LogInformation("File {FileId} uploaded ({Size} bytes)", session.File.Id, session.File.Size);

        return session.File.ToDto();
    }

    public async Task DiscardUploadAsync(UploadSession session)
    {
        await session.OutputStream.DisposeAsync();
        try
        {
            await storage.DeleteAsync(session.File.StoragePath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up partial upload {Path}", session.File.StoragePath);
        }
    }

    public async Task<PagedResult<FileDto>> ListAsync(Guid userId, Guid? folderId, string? search,
        string? typeFilter, string sort, bool ascending, int page, int pageSize, bool trashOnly,
        CancellationToken ct = default)
    {
        var result = await files.SearchAsync(new FileListQuery(
            OwnerId: userId, FolderId: folderId, Search: search, TypeFilter: typeFilter,
            Sort: sort, Ascending: ascending, Page: page, PageSize: pageSize, TrashOnly: trashOnly), ct);
        return PagedResult<FileDto>.Create(
            result.Items.Select(f => f.ToDto()).ToList(), result.Total, result.Page, result.PageSize);
    }

    public async Task<PagedResult<FileDto>> ListAllAsync(string? search, Guid? ownerId, string sort,
        bool ascending, int page, int pageSize, bool includeDeleted, CancellationToken ct = default)
    {
        var result = await files.SearchAsync(new FileListQuery(
            OwnerId: ownerId, Search: search, Sort: sort, Ascending: ascending,
            Page: page, PageSize: pageSize, IncludeDeleted: includeDeleted), ct);
        return PagedResult<FileDto>.Create(
            result.Items.Select(f => f.ToDto(f.Owner?.Username)).ToList(),
            result.Total, result.Page, result.PageSize);
    }

    public async Task<FileDto> GetAsync(Guid id, Guid requesterId, bool isAdmin, CancellationToken ct = default)
    {
        var file = await files.GetWithSharesAsync(id, ct);
        return AuthorizeAccess(file, requesterId, isAdmin).ToDto();
    }

    public async Task<FileDto> RenameAsync(Guid id, Guid requesterId, bool isAdmin, string newName,
        CancellationToken ct = default)
    {
        var file = AuthorizeAccess(await files.GetWithSharesAsync(id, ct), requesterId, isAdmin);

        var sanitized = Validators.SanitizeName(newName)
            ?? throw new ValidationException("File name is empty or contains only invalid characters.");
        if (Validators.IsReservedName(sanitized))
        {
            throw new ValidationException("That file name is reserved by the operating system.");
        }

        // Keep the original extension so the stored blob keeps matching metadata.
        var newExtension = Path.GetExtension(sanitized).ToLowerInvariant();
        if (!string.IsNullOrEmpty(file.Extension) && newExtension != file.Extension)
        {
            sanitized = Path.GetFileNameWithoutExtension(sanitized) + file.Extension;
        }

        if (string.Equals(file.Name, sanitized, StringComparison.Ordinal))
        {
            return file.ToDto();
        }

        var oldName = file.Name;
        file.Name = sanitized;
        file.UpdatedAt = DateTime.UtcNow;
        files.Update(file);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.FileRenamed,
            $"Renamed '{oldName}' to '{sanitized}'.", requesterId, "StoredFile", file.Id.ToString(), null, ct);
        return file.ToDto();
    }

    public async Task<FileDto> MoveAsync(Guid id, Guid requesterId, bool isAdmin, Guid? targetFolderId,
        CancellationToken ct = default)
    {
        var file = AuthorizeAccess(await files.GetWithSharesAsync(id, ct), requesterId, isAdmin);

        if (targetFolderId.HasValue)
        {
            var folder = await folders.GetByIdAsync(targetFolderId.Value, ct);
            if (folder is null || folder.IsDeleted || folder.OwnerId != file.OwnerId)
            {
                throw new NotFoundException("Target folder not found.");
            }
        }

        if (file.FolderId == targetFolderId)
        {
            return file.ToDto();
        }

        file.FolderId = targetFolderId;
        file.UpdatedAt = DateTime.UtcNow;
        files.Update(file);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.FileMoved,
            $"Moved '{file.Name}' to {(targetFolderId is null ? "root" : "another folder")}.",
            requesterId, "StoredFile", file.Id.ToString(), null, ct);
        return file.ToDto();
    }

    public async Task DeleteAsync(Guid id, Guid requesterId, bool isAdmin, string? ipAddress = null,
        CancellationToken ct = default)
    {
        var file = AuthorizeAccess(await files.GetWithSharesAsync(id, ct), requesterId, isAdmin);
        if (file.DeletedAt is not null)
        {
            return; // already trashed — idempotent
        }

        file.DeletedAt = DateTime.UtcNow;
        file.UpdatedAt = file.DeletedAt.Value;
        files.Update(file);

        // Trashed files are not reachable through their share links.
        foreach (var share in file.Shares.Where(s => s.IsActive))
        {
            share.IsActive = false;
        }

        await unitOfWork.SaveChangesAsync(ct);
        await activity.LogAsync(ActivityAction.FileDeleted,
            $"Moved '{file.Name}' to trash.", requesterId, "StoredFile", file.Id.ToString(), ipAddress, ct);
    }

    public async Task<FileDto> RestoreAsync(Guid id, Guid requesterId, bool isAdmin, CancellationToken ct = default)
    {
        var file = AuthorizeAccess(await files.GetWithSharesAsync(id, ct), requesterId, isAdmin);
        if (file.DeletedAt is null)
        {
            return file.ToDto();
        }

        // Restoring into a trashed parent folder is not allowed — fall back to root.
        if (file.FolderId is { } parentId)
        {
            var parent = await folders.GetByIdAsync(parentId, ct);
            if (parent is null || parent.IsDeleted)
            {
                file.FolderId = null;
            }
        }

        file.DeletedAt = null;
        file.UpdatedAt = DateTime.UtcNow;
        files.Update(file);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.FileRestored,
            $"Restored '{file.Name}' from trash.", requesterId, "StoredFile", file.Id.ToString(), null, ct);
        return file.ToDto();
    }

    public async Task DeletePermanentlyAsync(Guid id, Guid requesterId, bool isAdmin, string? ipAddress = null,
        CancellationToken ct = default)
    {
        var file = AuthorizeAccess(await files.GetWithSharesAsync(id, ct), requesterId, isAdmin);

        foreach (var share in file.Shares.ToList())
        {
            shares.Remove(share);
        }
        files.Remove(file);
        await unitOfWork.SaveChangesAsync(ct);

        // Physical destruction happens only after the metadata commit.
        await storage.DeleteAsync(file.StoragePath, CancellationToken.None);

        await activity.LogAsync(ActivityAction.FilePermanentlyDeleted,
            $"Permanently deleted '{file.Name}'.", requesterId, "StoredFile", file.Id.ToString(), ipAddress, ct);
        logger.LogInformation("File {FileId} permanently deleted", file.Id);
    }

    public async Task<DownloadTicket> AuthorizeDownloadAsync(Guid requesterId, Guid fileId, bool isAdmin,
        CancellationToken ct = default)
    {
        var file = AuthorizeAccess(await files.GetByIdAsync(fileId, ct), requesterId, isAdmin);
        if (file.DeletedAt is not null)
        {
            throw new NotFoundException("File not found.");
        }

        var stream = await storage.OpenReadAsync(file.StoragePath, ct);
        return new DownloadTicket { File = file, Stream = stream };
    }

    public async Task CompleteDownloadAsync(DownloadTicket ticket, string? ipAddress = null,
        CancellationToken ct = default)
    {
        var file = await files.GetByIdAsync(ticket.File.Id, ct);
        if (file is null)
        {
            return;
        }

        file.DownloadCount++;
        files.Update(file);
        await unitOfWork.SaveChangesAsync(ct);
        await activity.LogAsync(ActivityAction.FileDownloaded,
            $"Downloaded '{file.Name}'.", file.OwnerId, "StoredFile", file.Id.ToString(), ipAddress, ct);
    }

    private static StoredFile AuthorizeAccess(StoredFile? file, Guid requesterId, bool isAdmin)
    {
        if (file is null || (!isAdmin && file.OwnerId != requesterId))
        {
            // Not-found for foreign files avoids confirming existence to strangers.
            throw new NotFoundException("File not found.");
        }

        return file;
    }
}
