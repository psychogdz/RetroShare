using System.IO;
using System.Security.Claims;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetroShare.Application.Common;
using RetroShare.Application.Interfaces;
using RetroShare.Domain.Constants;
using FileTransferApi = RetroShare.Infrastructure.Grpc.FileTransfer.FileTransferBase;

namespace RetroShare.Infrastructure.Grpc;

/// <summary>gRPC data plane. Streams file bytes chunk-by-chunk without buffering whole
/// files in memory. Authentication reuses the JWT bearer setup (Authorization metadata);
/// share downloads are anonymous and validated per request.</summary>
public sealed class FileTransferGrpcService(
    IFileService fileService,
    IShareService shareService,
    IPermissionChecker permissionChecker,
    IOptions<StorageOptions> storageOptions,
    ILogger<FileTransferGrpcService> logger) : FileTransferApi
{
    private const int ChunkSize = FileRules.StreamingChunkSize;

    [Authorize]
    public override async Task<UploadResponse> Upload(IAsyncStreamReader<UploadRequest> requestStream,
        ServerCallContext context)
    {
        var http = context.GetHttpContext();
        var userId = GetUserId(http.User)
            ?? throw Unauthorized("Authentication required.");
        await RequirePermissionAsync(http.User, Permissions.FilesUpload, ct: context.CancellationToken);

        UploadSession? session = null;
        try
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                if (session is null)
                {
                    if (message.Init is null)
                    {
                        throw new RpcException(new Status(StatusCode.InvalidArgument,
                            "The first upload message must carry init metadata."));
                    }

                    session = await fileService.BeginUploadAsync(
                        userId,
                        message.Init.FileName,
                        message.Init.Size,
                        string.IsNullOrWhiteSpace(message.Init.MimeType) ? null : message.Init.MimeType,
                        Guid.TryParse(message.Init.FolderId, out var folderId) && message.Init.FolderId.Length > 0
                            ? folderId
                            : null,
                        context.CancellationToken);
                    continue;
                }

                if (message.Init is not null)
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument,
                        "Init metadata may only be sent once."));
                }

                if (message.Chunk.IsEmpty)
                {
                    continue;
                }

                session.BytesWritten += message.Chunk.Length;
                if (session.BytesWritten > storageOptions.Value.MaxFileSizeBytes)
                {
                    throw new RpcException(new Status(StatusCode.ResourceExhausted,
                        "Upload exceeds the maximum file size."));
                }

                await session.OutputStream.WriteAsync(message.Chunk.Memory, context.CancellationToken);
            }

            if (session is null)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "No upload data received."));
            }

            var dto = await fileService.CompleteUploadAsync(session, http.Connection.RemoteIpAddress?.ToString(),
                context.CancellationToken);
            logger.LogInformation("gRPC upload completed for {FileId}", dto.Id);
            return new UploadResponse { FileId = dto.Id.ToString(), TotalBytes = dto.Size };
        }
        catch (RpcException)
        {
            if (session is not null)
            {
                await fileService.DiscardUploadAsync(session);
            }

            throw;
        }
        catch (AppException ex)
        {
            if (session is not null)
            {
                await fileService.DiscardUploadAsync(session);
            }

            throw MapAppException(ex);
        }
        catch (IOException ex)
        {
            // The write path hit a filesystem failure (typically ENOSPC mid-stream);
            // discard the partial blob and surface a clean, non-leaking message.
            logger.LogError(ex, "gRPC upload failed with an I/O error (partial upload discarded)");
            if (session is not null)
            {
                await fileService.DiscardUploadAsync(session);
            }

            throw new RpcException(new Status(StatusCode.ResourceExhausted,
                "Insufficient storage space available."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "gRPC upload failed");
            if (session is not null)
            {
                await fileService.DiscardUploadAsync(session);
            }

            throw new RpcException(new Status(StatusCode.Internal, "Upload failed."));
        }
    }

    public override async Task Download(DownloadRequest request,
        IServerStreamWriter<DownloadResponse> responseStream, ServerCallContext context)
    {
        var http = context.GetHttpContext();
        DownloadTicket ticket;
        var isShareDownload = !string.IsNullOrWhiteSpace(request.ShareToken);

        // Authorization runs before any response frame is written so failures map to
        // precise gRPC status codes (NotFound / Unauthenticated / FailedPrecondition).
        try
        {
            if (isShareDownload)
            {
                ticket = await shareService.AuthorizeShareDownloadAsync(
                    request.ShareToken,
                    string.IsNullOrWhiteSpace(request.SharePassword) ? null : request.SharePassword,
                    http.Connection.RemoteIpAddress?.ToString(),
                    context.CancellationToken);
            }
            else
            {
                var userId = GetUserId(http.User)
                    ?? throw Unauthorized("Authentication required.");
                await RequirePermissionAsync(http.User, Permissions.FilesDownload, ct: context.CancellationToken);

                if (!Guid.TryParse(request.FileId, out var fileId))
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid file id."));
                }

                var isAdmin = await permissionChecker.HasPermissionAsync(http.User, Permissions.SystemManage,
                    context.CancellationToken);
                ticket = await fileService.AuthorizeDownloadAsync(userId, fileId, isAdmin, context.CancellationToken);
            }
        }
        catch (AppException ex)
        {
            throw MapAppException(ex);
        }

        try
        {
            await responseStream.WriteAsync(new DownloadResponse
            {
                Meta = new DownloadMeta
                {
                    FileName = ticket.File.Name,
                    Size = ticket.File.Size,
                    MimeType = ticket.File.MimeType,
                },
            }, context.CancellationToken);

            var buffer = new byte[ChunkSize];
            int read;
            while ((read = await ticket.Stream.ReadAsync(buffer, context.CancellationToken)) > 0)
            {
                await responseStream.WriteAsync(new DownloadResponse
                {
                    Chunk = Google.Protobuf.ByteString.CopyFrom(buffer, 0, read),
                }, context.CancellationToken);
            }

            if (!isShareDownload)
            {
                await fileService.CompleteDownloadAsync(ticket,
                    http.Connection.RemoteIpAddress?.ToString(), context.CancellationToken);
            }

            logger.LogInformation("gRPC download completed for {FileId}", ticket.File.Id);
        }
        finally
        {
            await ticket.Stream.DisposeAsync();
        }
    }

    private static Guid? GetUserId(ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private async Task RequirePermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct)
    {
        if (!await permissionChecker.HasPermissionAsync(user, permission, ct))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Missing permission: " + permission));
        }
    }

    private static RpcException Unauthorized(string message) =>
        new(new Status(StatusCode.Unauthenticated, message));

    private static RpcException MapAppException(AppException ex) => ex switch
    {
        ValidationException => new RpcException(new Status(StatusCode.InvalidArgument, ex.Message)),
        NotFoundException => new RpcException(new Status(StatusCode.NotFound, ex.Message)),
        ForbiddenException => new RpcException(new Status(StatusCode.PermissionDenied, ex.Message)),
        UnauthorizedException => new RpcException(new Status(StatusCode.Unauthenticated, ex.Message)),
        StorageLimitException => new RpcException(new Status(StatusCode.ResourceExhausted, ex.Message)),
        InsufficientStorageException => new RpcException(new Status(StatusCode.ResourceExhausted,
            "Insufficient storage space available.")),
        ShareAccessException sa => sa.ErrorCode switch
        {
            "SHARE_NOT_FOUND" => new RpcException(new Status(StatusCode.NotFound, sa.Message)),
            "SHARE_PASSWORD_REQUIRED" or "SHARE_INVALID_PASSWORD" =>
                new RpcException(new Status(StatusCode.Unauthenticated, sa.Message)),
            _ => new RpcException(new Status(StatusCode.FailedPrecondition, sa.Message)),
        },
        ConflictException => new RpcException(new Status(StatusCode.AlreadyExists, ex.Message)),
        _ => new RpcException(new Status(StatusCode.Internal, "Request failed.")),
    };
}
