namespace RetroShare.Application.Interfaces;

/// <summary>Physical blob storage abstraction. All paths are relative to the configured
/// storage root and are always generated server-side — never from user input.</summary>
public interface IFileStorage
{
    /// <summary>Relative path for a new blob, e.g. "users/{ownerId}/{fileId}".</summary>
    string BuildRelativePath(Guid ownerId, Guid fileId);

    /// <summary>Opens an output stream for a new blob, creating parent directories.</summary>
    Task<Stream> OpenWriteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>Opens a read stream for an existing blob.</summary>
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default);

    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);

    Task<long> GetSizeAsync(string relativePath, CancellationToken ct = default);

    /// <summary>Free space on the storage volume, used by the health check.</summary>
    Task<long?> GetFreeSpaceBytesAsync(CancellationToken ct = default);
}
