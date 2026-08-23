using RetroShare.Application.DTOs;

namespace RetroShare.Application.Interfaces;

/// <summary>A raw machine-level snapshot taken by the infrastructure layer. Null fields mean
/// the metric is unavailable on the current platform — the DTO keeps them absent rather than
/// reporting fabricated numbers.</summary>
public sealed record MachineSnapshot
{
    public double? CpuUsagePercent { get; init; }

    public double? RamUsagePercent { get; init; }
    public long? RamTotalBytes { get; init; }
    public long? RamUsedBytes { get; init; }

    public long DiskTotalBytes { get; init; }
    public long DiskFreeBytes { get; init; }

    public long UptimeSeconds { get; init; }
}

/// <summary>Cheap, cache-friendly access to host machine metrics. Implemented in
/// Infrastructure; no shell commands, no allocations beyond small structs.</summary>
public interface ISystemInfoProvider
{
    /// <summary>CPU usage is computed from a delta between calls, so the first invocation
    /// (or one immediately after start) may return null.</summary>
    MachineSnapshot GetMachineSnapshot();

    /// <summary>Physical size of the SQLite database file, or null for in-memory databases.</summary>
    long? GetDatabaseSizeBytes();

    /// <summary>Physical size of the blob storage directory (recursive, cached — safe to call
    /// from a polling loop on small machines).</summary>
    long GetStorageDirectorySizeBytes();
}

/// <summary>Assembles the admin monitoring DTO from machine metrics, repository totals and
/// configured disk-health thresholds.</summary>
public interface ISystemMonitorService
{
    Task<SystemMonitorDto> GetSnapshotAsync(CancellationToken ct = default);
}
