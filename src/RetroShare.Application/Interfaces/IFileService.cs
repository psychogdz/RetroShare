using RetroShare.Application.DTOs;
using RetroShare.Domain.Entities;

using RetroShare.Application.Common;

namespace RetroShare.Application.Interfaces;

/// <summary>Server-side state for an in-progress streamed upload. Created by
/// <see cref="IFileService.BeginUploadAsync"/> and completed or discarded by the caller.</summary>
public sealed class UploadSession
{
    public required Guid OwnerId { get; init; }

    /// <summary>Draft metadata; persisted only when the upload completes successfully.</summary>
    public required StoredFile File { get; init; }

    public required Stream OutputStream { get; init; }

    public required long DeclaredSize { get; init; }

    public long BytesWritten { get; set; }
}

/// <summary>Autorized, ready-to-stream download of a file or share.</summary>
public sealed class DownloadTicket
{
    public required StoredFile File { get; init; }

    public required Stream Stream { get; init; }

    /// <summary>Set when the download flows through a share link (counter already incremented).</summary>
    public Guid? ShareId { get; init; }
}

public interface IFileService
{
    /// <summary>Validates the announced upload (name, extension, MIME type, size, quota) and
    /// opens the storage target. Throws <see cref="Common.StorageLimitException"/> on quota or
    /// size violations before any bytes are written.</summary>
    Task<UploadSession> BeginUploadAsync(Guid ownerId, string fileName, long declaredSize,
        string? mimeType, Guid? folderId, CancellationToken ct = default);

    /// <summary>Persists metadata after the stream finished; verifies the byte count.</summary>
    Task<FileDto> CompleteUploadAsync(UploadSession session, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>Discards a failed/cancelled upload and removes partial data.</summary>
    Task DiscardUploadAsync(UploadSession session);

    /// <summary>Lists the user's files (optionally inside a folder, trash only, filtered).</summary>
    Task<PagedResult<FileDto>> ListAsync(Guid userId, Guid? folderId, string? search, string? typeFilter,
        string sort, bool ascending, int page, int pageSize, bool trashOnly, CancellationToken ct = default);

    /// <summary>Admin listing across all users.</summary>
    Task<PagedResult<FileDto>> ListAllAsync(string? search, Guid? ownerId, string sort, bool ascending,
        int page, int pageSize, bool includeDeleted, CancellationToken ct = default);

    Task<FileDto> GetAsync(Guid id, Guid requesterId, bool isAdmin, CancellationToken ct = default);

    Task<FileDto> RenameAsync(Guid id, Guid requesterId, bool isAdmin, string newName, CancellationToken ct = default);

    Task<FileDto> MoveAsync(Guid id, Guid requesterId, bool isAdmin, Guid? targetFolderId, CancellationToken ct = default);

    /// <summary>Soft delete: moves the file (and its share links' usability) to trash.</summary>
    Task DeleteAsync(Guid id, Guid requesterId, bool isAdmin, string? ipAddress = null, CancellationToken ct = default);

    Task<FileDto> RestoreAsync(Guid id, Guid requesterId, bool isAdmin, CancellationToken ct = default);

    /// <summary>Hard delete: removes metadata and the physical blob.</summary>
    Task DeletePermanentlyAsync(Guid id, Guid requesterId, bool isAdmin, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>Authorizes a direct download by the owner (or admin) and opens the blob.</summary>
    Task<DownloadTicket> AuthorizeDownloadAsync(Guid requesterId, Guid fileId, bool isAdmin, CancellationToken ct = default);

    /// <summary>Records a completed direct download (counter + activity).</summary>
    Task CompleteDownloadAsync(DownloadTicket ticket, string? ipAddress = null, CancellationToken ct = default);
}
