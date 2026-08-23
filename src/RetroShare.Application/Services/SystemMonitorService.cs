using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;

namespace RetroShare.Application.Services;

/// <summary>Composes live machine metrics with RetroShare data totals for the admin
/// monitoring page. All inputs are cheap: the provider caches the expensive parts.</summary>
public sealed class SystemMonitorService(
    ISystemInfoProvider systemInfo,
    IFileRepository files,
    IOptions<StorageOptions> storageOptions,
    ILogger<SystemMonitorService> logger) : ISystemMonitorService
{
    private readonly StorageOptions _options = storageOptions.Value;

    public async Task<SystemMonitorDto> GetSnapshotAsync(CancellationToken ct = default)
    {
        var machine = systemInfo.GetMachineSnapshot();
        var fileCount = await files.CountAllFilesAsync(ct);
        var storedBytes = await files.SumAllActiveBytesAsync(ct);

        var diskTotal = machine.DiskTotalBytes;
        var diskFree = machine.DiskFreeBytes;
        var diskUsed = Math.Max(0, diskTotal - diskFree);
        var diskPercent = diskTotal > 0 ? Math.Round(diskUsed * 100.0 / diskTotal, 1) : 0;

        logger.LogDebug("Monitoring snapshot: cpu={Cpu}%, ram={Ram}%, disk={Disk}%",
            machine.CpuUsagePercent, machine.RamUsagePercent, diskPercent);

        return new SystemMonitorDto
        {
            CpuUsagePercent = machine.CpuUsagePercent,
            RamUsagePercent = machine.RamUsagePercent,
            RamTotalBytes = machine.RamTotalBytes,
            RamUsedBytes = machine.RamUsedBytes,
            DiskTotalBytes = diskTotal,
            DiskUsedBytes = diskUsed,
            DiskFreeBytes = diskFree,
            DiskUsagePercent = diskPercent,
            DiskState = DiskHealth.Classify(
                diskPercent, _options.DiskWarningThresholdPercent, _options.DiskCriticalThresholdPercent),
            DiskWarningThresholdPercent = _options.DiskWarningThresholdPercent,
            DiskCriticalThresholdPercent = _options.DiskCriticalThresholdPercent,
            UptimeSeconds = machine.UptimeSeconds,
            FileCount = fileCount,
            StoredBytes = storedBytes,
            StorageDirectoryBytes = systemInfo.GetStorageDirectorySizeBytes(),
            DatabaseSizeBytes = systemInfo.GetDatabaseSizeBytes(),
        };
    }
}
