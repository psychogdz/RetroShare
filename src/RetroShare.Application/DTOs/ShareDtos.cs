using System.ComponentModel.DataAnnotations;

namespace RetroShare.Application.DTOs;

public class CreateShareRequest
{
    [Required]
    public Guid FileId { get; set; }

    /// <summary>UTC expiration; null means the link never expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Optional password protecting the link.</summary>
    [MinLength(4), MaxLength(128)]
    public string? Password { get; set; }

    /// <summary>Maximum number of downloads; null means unlimited.</summary>
    [Range(1, 1_000_000)]
    public int? MaxDownloads { get; set; }
}

public sealed class ShareDto
{
    public required Guid Id { get; init; }

    /// <summary>The public share token (safe to expose; it is the shared secret itself).</summary>
    public required string Token { get; init; }

    public required Guid FileId { get; init; }

    public required string FileName { get; init; }

    public required long FileSize { get; init; }

    public DateTime? ExpiresAt { get; init; }

    public int? MaxDownloads { get; init; }

    public int DownloadCount { get; init; }

    public bool IsActive { get; init; }

    public bool HasPassword { get; init; }

    public required DateTime CreatedAt { get; init; }

    public bool IsExpired { get; init; }
}

/// <summary>Public metadata for a share link — no owner identity, no token hash internals.</summary>
public sealed class PublicShareInfoDto
{
    public required string Token { get; init; }

    public required string FileName { get; init; }

    public required long FileSize { get; init; }

    public bool RequiresPassword { get; init; }

    public DateTime? ExpiresAt { get; init; }

    public bool IsAvailable { get; init; }
}

public class ShareVerifyRequest
{
    [MinLength(1), MaxLength(128)]
    public string? Password { get; set; }
}
