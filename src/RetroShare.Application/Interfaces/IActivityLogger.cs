using RetroShare.Domain.Enums;

namespace RetroShare.Application.Interfaces;

/// <summary>Writes audit entries for important actions. Descriptions must never contain
/// passwords, token secrets or filesystem paths.</summary>
public interface IActivityLogger
{
    Task LogAsync(ActivityAction action, string description, Guid? userId = null,
        string? entityType = null, string? entityId = null, string? ipAddress = null,
        CancellationToken ct = default);
}
