using System.ComponentModel.DataAnnotations;
using RetroShare.Domain.Constants;

using RetroShare.Application.Common;

namespace RetroShare.Application.DTOs;

public sealed class FileDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required long Size { get; init; }

    public required string MimeType { get; init; }

    public required string Extension { get; init; }

    public Guid? FolderId { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; init; }

    public DateTime? DeletedAt { get; init; }

    public bool IsDeleted { get; init; }

    public long DownloadCount { get; init; }

    public int ActiveShareCount { get; init; }

    /// <summary>Owner username; only populated in admin listings.</summary>
    public string? OwnerUsername { get; init; }
}

public class RenameFileRequest
{
    [Required, MinLength(1), MaxLength(FileRules.MaxNameLength)]
    public string Name { get; set; } = string.Empty;
}

public class MoveFileRequest
{
    /// <summary>Target folder, or null to move to the root.</summary>
    public Guid? FolderId { get; set; }
}

public class CreateFolderRequest
{
    [Required, MinLength(1), MaxLength(FileRules.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }
}

public class RenameFolderRequest
{
    [Required, MinLength(1), MaxLength(FileRules.MaxNameLength)]
    public string Name { get; set; } = string.Empty;
}

public class MoveFolderRequest
{
    /// <summary>New parent folder, or null to move to the root.</summary>
    public Guid? ParentId { get; set; }
}

public sealed class FolderDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public Guid? ParentId { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; init; }
}

public sealed class FolderBreadcrumb
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }
}

public sealed class FolderContentsDto
{
    public required IReadOnlyList<FolderBreadcrumb> Breadcrumbs { get; init; }

    public required IReadOnlyList<FolderDto> Folders { get; init; }

    public required PagedResult<FileDto> Files { get; init; }
}
