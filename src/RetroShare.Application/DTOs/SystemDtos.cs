namespace RetroShare.Application.DTOs;

/// <summary>Live server resource snapshot for the admin monitoring page. Contains only
/// derived metrics — never paths, environment data or credentials.</summary>
public sealed class SystemMonitorDto
{
    /// <summary>UTC moment the snapshot was taken.</summary>
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    // CPU
    public double? CpuUsagePercent { get; init; }

    // Memory
    public double? RamUsagePercent { get; init; }
    public long? RamTotalBytes { get; init; }
    public long? RamUsedBytes { get; init; }

    // Disk (the volume hosting RetroShare storage)
    public long DiskTotalBytes { get; init; }
    public long DiskUsedBytes { get; init; }
    public long DiskFreeBytes { get; init; }
    public double DiskUsagePercent { get; init; }

    /// <summary>"Healthy", "Warning" or "Critical" per configured thresholds.</summary>
    public string DiskState { get; init; } = "Healthy";

    /// <summary>Configured thresholds, in percent, so the UI can label them.</summary>
    public int DiskWarningThresholdPercent { get; init; }
    public int DiskCriticalThresholdPercent { get; init; }

    /// <summary>OS uptime in seconds.</summary>
    public long UptimeSeconds { get; init; }

    // RetroShare data footprint
    /// <summary>Number of stored file records (active, not trash).</summary>
    public long FileCount { get; init; }

    /// <summary>Total logical size of active files per the database.</summary>
    public long StoredBytes { get; init; }

    /// <summary>Physical size of the blob directory on disk (cached scan).</summary>
    public long? StorageDirectoryBytes { get; init; }

    /// <summary>Physical size of the SQLite database file.</summary>
    public long? DatabaseSizeBytes { get; init; }
}
