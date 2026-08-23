using RetroShare.Domain.Enums;

namespace RetroShare.Domain.Entities;

/// <summary>An audit entry for a security- or content-relevant action. Never contains
/// passwords, token secrets or filesystem paths.</summary>
public class ActivityLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? UserId { get; set; }

    public ActivityAction Action { get; set; }

    /// <summary>Human-readable summary safe for display, e.g. "Uploaded 'report.pdf' (2.1 MB)".</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional target entity type, e.g. "StoredFile".</summary>
    public string? EntityType { get; set; }

    /// <summary>Optional target entity identifier.</summary>
    public string? EntityId { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
