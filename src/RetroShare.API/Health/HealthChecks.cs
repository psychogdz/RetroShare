using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RetroShare.Application.Interfaces;
using RetroShare.Infrastructure.Data;

namespace RetroShare.API.Health;

/// <summary>Verifies the SQLite database answers a trivial query.</summary>
public sealed class DatabaseHealthCheck(AppDbContext db, ILogger<DatabaseHealthCheck> logger)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database reachable")
                : HealthCheckResult.Unhealthy("Database unreachable");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database health check failed");
            return HealthCheckResult.Unhealthy("Database check threw an exception");
        }
    }
}

/// <summary>Verifies the blob storage root exists, is writable and reports free space.</summary>
public sealed class StorageHealthCheck(IFileStorage storage, ILogger<StorageHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var probe = $"health/{Guid.NewGuid():N}.probe";
            await using (var stream = await storage.OpenWriteAsync(probe, cancellationToken))
            {
                await stream.WriteAsync(new byte[] { 0x52, 0x53 }, cancellationToken); // "RS"
            }

            var exists = await storage.ExistsAsync(probe, cancellationToken);
            await storage.DeleteAsync(probe, cancellationToken);

            if (!exists)
            {
                return HealthCheckResult.Degraded("Storage probe write could not be verified");
            }

            var free = await storage.GetFreeSpaceBytesAsync(cancellationToken);
            return HealthCheckResult.Healthy("Storage writable",
                data: free.HasValue
                    ? new Dictionary<string, object> { ["freeBytes"] = free.Value }
                    : null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Storage health check failed");
            return HealthCheckResult.Unhealthy("Storage is not writable");
        }
    }
}

/// <summary>Writes the standard health-check JSON response for GET /api/health.</summary>
public static class HealthResponseWriter
{
    public static async Task WriteJson(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    durationMs = e.Value.Duration.TotalMilliseconds,
                    data = e.Value.Data.Count == 0 ? null : e.Value.Data,
                }),
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
