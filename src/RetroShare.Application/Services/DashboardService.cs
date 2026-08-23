using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Application.Mapping;
using RetroShare.Domain.Enums;

namespace RetroShare.Application.Services;

/// <summary>Aggregates dashboard statistics for users and administrators.</summary>
public sealed class DashboardService(
    IFileRepository files,
    IActivityRepository activityLog,
    IUserRepository users) : IDashboardService
{
    public async Task<DashboardDto> GetUserDashboardAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("User not found.");

        var activeFiles = await files.CountByOwnerAsync(userId, includeDeleted: false, ct);
        var allFiles = await files.CountByOwnerAsync(userId, includeDeleted: true, ct);
        var usedBytes = await files.SumActiveBytesAsync(userId, ct);
        var shareCount = await files.CountSharesByOwnerAsync(userId, ct);
        var recentFiles = await files.ListRecentByOwnerAsync(userId, 6, ct);
        var recentActivity = await activityLog.ListByUserAsync(userId, 1, 8, ct);

        return new DashboardDto
        {
            TotalFiles = activeFiles,
            StorageUsedBytes = usedBytes,
            StorageQuotaBytes = user.StorageQuotaBytes,
            TrashCount = allFiles - activeFiles,
            ShareCount = shareCount,
            RecentFiles = recentFiles.Select(f => f.ToDto()).ToList(),
            RecentActivity = recentActivity.Items.Select(a => a.ToDto()).ToList(),
        };
    }

    public async Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken ct = default)
    {
        var totalUsers = await files.CountAllUsersAsync(ct);
        var totalFiles = await files.CountAllFilesAsync(ct);
        var totalBytes = await files.SumAllActiveBytesAsync(ct);
        var totalShares = await files.CountAllSharesAsync(ct);
        var recentActivity = await activityLog.ListAllAsync(null, null, 1, 10, ct);

        return new AdminDashboardDto
        {
            TotalUsers = totalUsers,
            TotalFiles = totalFiles,
            TotalBytesStored = totalBytes,
            TotalShares = totalShares,
            ActiveShares = totalShares,
            RecentActivity = recentActivity.Items.Select(a => a.ToDto()).ToList(),
        };
    }
}

public sealed class ActivityService(IActivityRepository activityLog, IUserRepository users) : IActivityService
{
    public async Task<PagedResult<ActivityDto>> ListOwnAsync(Guid userId, int page, int pageSize,
        CancellationToken ct = default)
    {
        var result = await activityLog.ListByUserAsync(userId, page, pageSize, ct);
        return PagedResult<ActivityDto>.Create(
            result.Items.Select(a => a.ToDto()).ToList(), result.Total, result.Page, result.PageSize);
    }

    public async Task<PagedResult<ActivityDto>> ListAllAsync(ActivityAction? action, Guid? userId,
        int page, int pageSize, CancellationToken ct = default)
    {
        var result = await activityLog.ListAllAsync(action, userId, page, pageSize, ct);
        var usernames = new Dictionary<Guid, string>();
        foreach (var entry in result.Items.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value).Distinct())
        {
            var user = await users.GetByIdAsync(entry, ct);
            if (user is not null)
            {
                usernames[entry] = user.Username;
            }
        }

        return PagedResult<ActivityDto>.Create(
            result.Items.Select(a => a.ToDto(usernames.GetValueOrDefault(a.UserId ?? Guid.Empty))).ToList(),
            result.Total, result.Page, result.PageSize);
    }
}

/// <summary>Writes activity entries through the repository; a thin helper so every service
/// logs with the same shape. The logger commits its own save so callers do not need a
/// second SaveChanges after their main transaction.</summary>
public sealed class ActivityLogger(IActivityRepository repository, IUnitOfWork unitOfWork) : IActivityLogger
{
    public async Task LogAsync(ActivityAction action, string description, Guid? userId = null,
        string? entityType = null, string? entityId = null, string? ipAddress = null,
        CancellationToken ct = default)
    {
        await repository.AddAsync(new Domain.Entities.ActivityLog
        {
            Action = action,
            Description = description,
            UserId = userId,
            EntityType = entityType,
            EntityId = entityId,
            IpAddress = ipAddress,
        }, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
