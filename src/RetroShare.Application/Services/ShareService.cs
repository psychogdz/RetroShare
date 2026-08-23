using Microsoft.Extensions.Logging;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Application.Mapping;
using RetroShare.Application.Validation;
using RetroShare.Domain.Entities;
using RetroShare.Domain.Enums;

namespace RetroShare.Application.Services;

/// <summary>Share-link lifecycle: creation with expiration/password/download limits,
/// revocation and anonymous download authorization with atomic counter increments.</summary>
public sealed class ShareService(
    IShareRepository shares,
    IFileRepository files,
    IFileStorage storage,
    IPasswordHasher passwordHasher,
    ISecureTokenGenerator tokenGenerator,
    IActivityLogger activity,
    IUnitOfWork unitOfWork,
    ILogger<ShareService> logger) : IShareService
{
    private const int TokenBytes = 16; // 128-bit share tokens

    public async Task<ShareDto> CreateAsync(Guid userId, CreateShareRequest request, CancellationToken ct = default)
    {
        var file = await files.GetWithSharesAsync(request.FileId, ct);
        if (file is null || file.DeletedAt is not null || file.OwnerId != userId)
        {
            throw new NotFoundException("File not found.");
        }

        if (!string.IsNullOrWhiteSpace(request.Password) && request.Password.Length < 4)
        {
            throw new ValidationException("Share password must be at least 4 characters.");
        }

        Validators.ValidateShareOptions(request.ExpiresAt, request.MaxDownloads);

        var share = new ShareLink
        {
            FileId = file.Id,
            CreatedBy = userId,
            Token = tokenGenerator.GenerateToken(TokenBytes),
            PasswordHash = string.IsNullOrWhiteSpace(request.Password)
                ? null
                : passwordHasher.Hash(request.Password!),
            ExpiresAt = request.ExpiresAt?.ToUniversalTime(),
            MaxDownloads = request.MaxDownloads,
            IsActive = true,
        };

        shares.Add(share);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.ShareCreated,
            $"Created share link for '{file.Name}'.", userId, "ShareLink", share.Id.ToString(), null, ct);
        logger.LogInformation("Share {ShareId} created for file {FileId}", share.Id, file.Id);

        return share.ToDto();
    }

    public async Task<PagedResult<ShareDto>> ListOwnAsync(Guid userId, int page, int pageSize,
        CancellationToken ct = default)
    {
        var result = await shares.ListByOwnerAsync(userId, page, pageSize, ct);
        return PagedResult<ShareDto>.Create(
            result.Items.Select(s => s.ToDto()).ToList(), result.Total, result.Page, result.PageSize);
    }

    public async Task<PagedResult<ShareDto>> ListAllAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var result = await shares.ListAllAsync(page, pageSize, ct);
        return PagedResult<ShareDto>.Create(
            result.Items.Select(s => s.ToDto()).ToList(), result.Total, result.Page, result.PageSize);
    }

    public async Task RevokeAsync(Guid shareId, Guid requesterId, bool isAdmin, string? ipAddress = null,
        CancellationToken ct = default)
    {
        var share = await shares.GetByIdAsync(shareId, ct)
            ?? throw ShareAccessException.NotFound();

        if (!isAdmin && share.CreatedBy != requesterId)
        {
            throw new NotFoundException("Share link not found.");
        }

        if (!share.IsActive)
        {
            return; // idempotent
        }

        share.IsActive = false;
        shares.Update(share);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.ShareRevoked,
            "Revoked a share link.", requesterId, "ShareLink", share.Id.ToString(), ipAddress, ct);
        logger.LogInformation("Share {ShareId} revoked", shareId);
    }

    public async Task<PublicShareInfoDto> GetPublicInfoAsync(string token, CancellationToken ct = default)
    {
        var share = await shares.GetByTokenWithFileAsync(token, ct)
            ?? throw ShareAccessException.NotFound();

        if (share.File.DeletedAt is not null)
        {
            throw ShareAccessException.Unavailable("the shared file was deleted");
        }

        var available = share.IsUsable(DateTime.UtcNow);
        return new PublicShareInfoDto
        {
            Token = share.Token,
            FileName = share.File.Name,
            FileSize = share.File.Size,
            RequiresPassword = share.PasswordHash is not null,
            ExpiresAt = share.ExpiresAt,
            IsAvailable = available,
        };
    }

    public async Task<DownloadTicket> AuthorizeShareDownloadAsync(string token, string? password,
        string? ipAddress = null, CancellationToken ct = default)
    {
        var share = await shares.GetByTokenWithFileAsync(token, ct)
            ?? throw ShareAccessException.NotFound();

        if (share.File.DeletedAt is not null)
        {
            throw ShareAccessException.Unavailable("the shared file was deleted");
        }

        if (!share.IsActive)
        {
            throw ShareAccessException.Unavailable("the link was revoked");
        }

        if (share.ExpiresAt is not null && share.ExpiresAt <= DateTime.UtcNow)
        {
            throw ShareAccessException.Unavailable("the link expired");
        }

        if (share.MaxDownloads is not null && share.DownloadCount >= share.MaxDownloads.Value)
        {
            throw ShareAccessException.Unavailable("the download limit was reached");
        }

        if (share.PasswordHash is not null)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw ShareAccessException.PasswordRequired();
            }

            if (!passwordHasher.Verify(share.PasswordHash, password))
            {
                throw ShareAccessException.WrongPassword();
            }
        }

        // Increment before streaming so a concurrent request cannot exceed the limit.
        share.DownloadCount++;
        share.File.DownloadCount++;
        shares.Update(share);
        files.Update(share.File);
        await unitOfWork.SaveChangesAsync(ct);

        var stream = await storage.OpenReadAsync(share.File.StoragePath, ct);
        await activity.LogAsync(ActivityAction.ShareDownloaded,
            $"Downloaded shared file '{share.File.Name}'.", share.CreatedBy,
            "StoredFile", share.File.Id.ToString(), ipAddress, ct);

        return new DownloadTicket { File = share.File, Stream = stream, ShareId = share.Id };
    }
}
