using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetroShare.Application.Common;
using RetroShare.Application.Interfaces;
using RetroShare.Infrastructure.Data;

namespace RetroShare.Infrastructure.Monitoring;

/// <summary>Host machine metrics without shell commands. Reads /proc files on Linux (the
/// production container); every metric has a cross-platform fallback. CPU usage is a delta
/// between consecutive calls and the storage-directory scan is cached, so polling every few
/// seconds stays cheap on small VPS machines.</summary>
public sealed class SystemInfoProvider(
    IConfiguration configuration,
    IHostEnvironment environment,
    IOptions<StorageOptions> storageOptions,
    ILogger<SystemInfoProvider> logger) : ISystemInfoProvider
{
    private static readonly TimeSpan StorageScanInterval = TimeSpan.FromSeconds(60);

    private readonly string _storageRoot = storageOptions.Value.Root;
    private readonly string? _databaseFile =
        SqlitePathResolver.ResolveDatabaseFile(configuration, environment.ContentRootPath);

    // CPU delta state (singleton instance).
    private readonly object _cpuLock = new();
    private (long Idle, long Total)? _lastCpuSample;

    // Storage scan cache.
    private readonly object _scanLock = new();
    private long _storageDirectoryBytes;
    private DateTimeOffset _storageScanAt = DateTimeOffset.MinValue;

    public MachineSnapshot GetMachineSnapshot()
    {
        var drive = new DriveInfo(Path.GetPathRoot(_storageRoot) ?? _storageRoot);
        return new MachineSnapshot
        {
            CpuUsagePercent = GetCpuUsagePercent(),
            RamUsagePercent = GetRamUsagePercent(),
            RamTotalBytes = GetRamTotalBytes(),
            RamUsedBytes = GetRamUsedBytes(),
            DiskTotalBytes = drive.IsReady ? drive.TotalSize : 0,
            DiskFreeBytes = drive.IsReady ? drive.AvailableFreeSpace : 0,
            UptimeSeconds = GetUptimeSeconds(),
        };
    }

    public long? GetDatabaseSizeBytes()
    {
        try
        {
            return _databaseFile is not null && File.Exists(_databaseFile)
                ? new FileInfo(_databaseFile).Length
                : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not determine database file size");
            return null;
        }
    }

    public long GetStorageDirectorySizeBytes()
    {
        lock (_scanLock)
        {
            if (DateTimeOffset.UtcNow - _storageScanAt < StorageScanInterval)
            {
                return _storageDirectoryBytes;
            }

            try
            {
                var dir = new DirectoryInfo(_storageRoot);
                _storageDirectoryBytes = dir.Exists
                    ? dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                    : 0;
                _storageScanAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                // Keep the previous value; the disk may be mid-change.
                logger.LogDebug(ex, "Storage directory scan failed");
            }

            return _storageDirectoryBytes;
        }
    }

    private double? GetCpuUsagePercent()
    {
        var sample = ReadProcStat();
        if (sample is null)
        {
            return null;
        }

        lock (_cpuLock)
        {
            var previous = _lastCpuSample;
            _lastCpuSample = sample;
            if (previous is null)
            {
                return null; // first call — no delta yet
            }

            var idleDelta = sample.Value.Idle - previous.Value.Idle;
            var totalDelta = sample.Value.Total - previous.Value.Total;
            return totalDelta > 0 ? Math.Round(Math.Max(0, 100 - idleDelta * 100.0 / totalDelta), 1) : null;
        }
    }

    private static (long Idle, long Total)? ReadProcStat()
    {
        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault();
            if (line is null || !line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                return null;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // cpu user nice system idle iowait irq softirq steal guest guest_nice
            var values = parts.Skip(1).Select(long.Parse).ToArray();
            var idle = values.Length > 3 ? values[3] + (values.Length > 4 ? values[4] : 0) : 0;
            var total = values.Sum();
            return (idle, total);
        }
        catch (Exception)
        {
            return null; // not Linux (or /proc unavailable) — CPU stays unknown
        }
    }

    private long? GetRamTotalBytes()
    {
        var meminfo = ReadProcMeminfo();
        return meminfo.TryGetValue("MemTotal", out var kb) ? kb * 1024 : null;
    }

    private long? GetRamUsedBytes()
    {
        var meminfo = ReadProcMeminfo();
        if (!meminfo.TryGetValue("MemTotal", out var totalKb))
        {
            return null;
        }

        var availableKb = meminfo.TryGetValue("MemAvailable", out var avail) ? avail : 0;
        return Math.Max(0, totalKb - availableKb) * 1024;
    }

    private double? GetRamUsagePercent()
    {
        var total = GetRamTotalBytes();
        var used = GetRamUsedBytes();
        return total is > 0 && used.HasValue ? Math.Round(used.Value * 100.0 / total.Value, 1) : null;
    }

    private static Dictionary<string, long> ReadProcMeminfo()
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                var idx = line.IndexOf(':');
                if (idx <= 0)
                {
                    continue;
                }

                var key = line[..idx];
                var valuePart = line[(idx + 1)..].Trim();
                var space = valuePart.IndexOf(' ');
                var number = space > 0 ? valuePart[..space] : valuePart;
                if (long.TryParse(number, out var kb))
                {
                    result[key] = kb;
                }
            }
        }
        catch (Exception)
        {
            // not Linux — memory stays unknown
        }

        return result;
    }

    private static long GetUptimeSeconds()
    {
        try
        {
            var first = File.ReadLines("/proc/uptime").FirstOrDefault();
            if (first is not null && double.TryParse(first.AsSpan(0, first.IndexOf(' ')), out var seconds))
            {
                return (long)seconds;
            }
        }
        catch (Exception)
        {
            // fall through to process uptime
        }

        return Environment.TickCount64 / 1000;
    }
}
