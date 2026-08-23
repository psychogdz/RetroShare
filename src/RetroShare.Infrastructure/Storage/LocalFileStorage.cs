using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetroShare.Application.Common;
using RetroShare.Application.Interfaces;

namespace RetroShare.Infrastructure.Storage;

/// <summary>Physical blob storage on the local filesystem. Blobs live under a generated
/// path (<c>users/{ownerId}/{fileId}</c>) inside the configured root. Every path is
/// validated against traversal before touching the disk.</summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options, ILogger<LocalFileStorage> logger) : IFileStorage
{
    private readonly StorageOptions _options = options.Value;
    private string Root => Path.GetFullPath(_options.Root,
        Environment.CurrentDirectory);

    public string BuildRelativePath(Guid ownerId, Guid fileId) => $"users/{ownerId:N}/{fileId:N}";

    public async Task<Stream> OpenWriteAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = SafeCombine(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return await Task.FromResult<Stream>(
            new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, useAsync: true));
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = SafeCombine(relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Stored blob is missing.", fullPath);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = SafeCombine(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            TryDeleteEmptyParent(Path.GetDirectoryName(fullPath));
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(SafeCombine(relativePath)));

    public Task<long> GetSizeAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = SafeCombine(relativePath);
        return Task.FromResult(File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0L);
    }

    public Task<long?> GetFreeSpaceBytesAsync(CancellationToken ct = default)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Root) ?? Root);
            return Task.FromResult<long?>(drive.AvailableFreeSpace);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not determine free space for {Root}", Root);
            return Task.FromResult<long?>(null);
        }
    }

    /// <summary>Combines the storage root with a relative path and verifies the result stays
    /// inside the root — the single choke point for path-traversal protection.</summary>
    private string SafeCombine(string relativePath)
    {
        var root = Root;
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(root, StringComparison.Ordinal))
        {
            logger.LogError("Rejected path outside storage root: {Path}", relativePath);
            throw new UnauthorizedAccessException("Invalid storage path.");
        }

        return full;
    }

    private void TryDeleteEmptyParent(string? directory)
    {
        try
        {
            if (directory is null)
            {
                return;
            }

            var dir = new DirectoryInfo(directory);
            if (dir.Exists && dir.Parent is not null && !dir.EnumerateFileSystemInfos().Any())
            {
                dir.Delete();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not prune empty directory {Directory}", directory);
        }
    }
}
