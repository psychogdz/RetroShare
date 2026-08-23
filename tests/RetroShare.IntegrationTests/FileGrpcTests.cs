using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using RetroShare.Infrastructure.Grpc;
using Xunit;
using FileTransferClient = RetroShare.Infrastructure.Grpc.FileTransfer.FileTransferClient;

namespace RetroShare.IntegrationTests;

/// <summary>gRPC data-plane tests over the in-memory test server channel.</summary>
public class FileGrpcTests(RetroShareFactory factory) : ApiTestBase(factory)
{
    private FileTransferClient NewClient() => new(GrpcChannel.ForAddress(Client.BaseAddress!, new GrpcChannelOptions
    {
        HttpClient = Factory.CreateClient(),
    }));

    private static Metadata Auth(string accessToken) => new() { { "Authorization", $"Bearer {accessToken}" } };

    /// <summary>Streams an upload through the real client-streaming API and returns the response.</summary>
    private static async Task<UploadResponse> UploadAsync(
        FileTransferClient client, string accessToken, string name, byte[] payload, string mime = "application/octet-stream")
    {
        var call = client.Upload(headers: Auth(accessToken));
        await call.RequestStream.WriteAsync(new UploadRequest
        {
            Init = new UploadInit { FileName = name, Size = payload.Length, MimeType = mime },
        });
        foreach (var chunk in Chunk(payload, 128 * 1024))
        {
            await call.RequestStream.WriteAsync(new UploadRequest
            {
                Chunk = Google.Protobuf.ByteString.CopyFrom(chunk),
            });
        }

        await call.RequestStream.CompleteAsync();
        return await call;
    }

    private static async Task<byte[]> DownloadAsync(FileTransferClient client, string accessToken, Guid fileId)
    {
        var call = client.Download(new DownloadRequest { FileId = fileId.ToString() }, headers: Auth(accessToken));
        var buffer = new MemoryStream();
        await foreach (var message in call.ResponseStream.ReadAllAsync())
        {
            if (message.Meta != null) continue;
            if (!message.Chunk.IsEmpty) message.Chunk.WriteTo(buffer);
        }

        return buffer.ToArray();
    }

    private static IEnumerable<byte[]> Chunk(byte[] source, int size)
    {
        for (var offset = 0; offset < source.Length; offset += size)
        {
            yield return source[offset..Math.Min(offset + size, source.Length)];
        }
    }

    [Fact]
    public async Task Upload_Then_Download_Roundtrips_Bytes()
    {
        var user = await RegisterAsync("grpcuser1");
        var client = NewClient();
        var payload = RandomBytes(150_000); // forces multiple 64 KB chunks server-side

        var response = await UploadAsync(client, user.AccessToken, "roundtrip.bin", payload);
        Assert.Equal(payload.Length, response.TotalBytes);
        Assert.False(string.IsNullOrEmpty(response.FileId));

        // Metadata is visible through the REST control plane.
        var meta = await Client.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{response.FileId}", user.AccessToken));
        meta.EnsureSuccessStatusCode();
        var file = await meta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("roundtrip.bin", file.GetProperty("name").GetString());
        Assert.Equal(payload.Length, file.GetProperty("size").GetInt64());

        // Download streams the identical bytes back.
        var downloaded = await DownloadAsync(client, user.AccessToken, Guid.Parse(response.FileId));
        Assert.Equal(payload, downloaded);
    }

    [Fact]
    public async Task Upload_ExeExtension_Rejected()
    {
        var user = await RegisterAsync("grpcuser2");
        var call = NewClient().Upload(headers: Auth(user.AccessToken));
        await call.RequestStream.WriteAsync(new UploadRequest
        {
            Init = new UploadInit { FileName = "malware.exe", Size = 10 },
        });
        await call.RequestStream.CompleteAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await call);
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Upload_DeclaredSizeMismatch_DiscardsUpload()
    {
        var user = await RegisterAsync("grpcuser3");
        var call = NewClient().Upload(headers: Auth(user.AccessToken));
        await call.RequestStream.WriteAsync(new UploadRequest
        {
            Init = new UploadInit { FileName = "short.txt", Size = 100 },
        });
        await call.RequestStream.WriteAsync(new UploadRequest
        {
            Chunk = Google.Protobuf.ByteString.CopyFrom("only ten!"u8),
        });
        await call.RequestStream.CompleteAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await call);
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);

        var list = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/files", user.AccessToken));
        var page = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, page.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Upload_WithoutAuth_Unauthenticated()
    {
        var call = NewClient().Upload();
        await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await call.RequestStream.WriteAsync(new UploadRequest
            {
                Init = new UploadInit { FileName = "anon.txt", Size = 1 },
            });
            await call.RequestStream.CompleteAsync();
            await call;
        });
    }

    [Fact]
    public async Task Download_ByOtherUser_NotFound()
    {
        var owner = await RegisterAsync("owneruser");
        var uploaded = await UploadAsync(NewClient(), owner.AccessToken, "private.txt", "hello"u8.ToArray());

        var stranger = await RegisterAsync("strangeruser");
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await DownloadAsync(NewClient(), stranger.AccessToken, Guid.Parse(uploaded.FileId)));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Admin_Can_Download_AnyFile()
    {
        var owner = await RegisterAsync("owneruser2");
        var uploaded = await UploadAsync(NewClient(), owner.AccessToken, "for-admin.txt", "admin"u8.ToArray());

        var admin = await LoginAsync("admin");
        var downloaded = await DownloadAsync(NewClient(), admin.AccessToken, Guid.Parse(uploaded.FileId));
        Assert.Equal("admin"u8.ToArray(), downloaded);
    }

    private static byte[] RandomBytes(int length)
    {
        var data = new byte[length];
        Random.Shared.NextBytes(data);
        return data;
    }
}

public class FileManagementTests(RetroShareFactory factory) : ApiTestBase(factory)
{
    private async Task<string> UploadSmallFileAsync(string token, string name, byte[] bytes)
    {
        var channel = GrpcChannel.ForAddress(Client.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = Factory.CreateClient(),
        });
        var client = new FileTransferClient(channel);
        var call = client.Upload(headers: new Metadata { { "Authorization", $"Bearer {token}" } });
        await call.RequestStream.WriteAsync(new UploadRequest
        {
            Init = new UploadInit { FileName = name, Size = bytes.Length, MimeType = "text/plain" },
        });
        await call.RequestStream.WriteAsync(new UploadRequest
        {
            Chunk = Google.Protobuf.ByteString.CopyFrom(bytes),
        });
        await call.RequestStream.CompleteAsync();
        return (await call).FileId;
    }

    [Fact]
    public async Task Rename_Search_Filter_Sort_And_Trash_Restore()
    {
        var user = await RegisterAsync("fileops");
        var first = await UploadSmallFileAsync(user.AccessToken, "report.pdf", "pdf-bytes"u8.ToArray());
        var second = await UploadSmallFileAsync(user.AccessToken, "photo.png", "png-bytes!"u8.ToArray());
        var third = await UploadSmallFileAsync(user.AccessToken, "notes.txt", "txt"u8.ToArray());

        // Rename keeps the extension.
        var rename = await Client.SendAsync(Authorized(HttpMethod.Put, $"/api/files/{first}", user.AccessToken,
            JsonBody(new { name = "annual report" })));
        rename.EnsureSuccessStatusCode();
        var renamed = await rename.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("annual report.pdf", renamed.GetProperty("name").GetString());

        // Search narrows by name.
        var search = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/files?search=photo", user.AccessToken));
        var searchPage = await search.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, searchPage.GetProperty("total").GetInt64());

        // Type filter narrows by category.
        var images = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/files?type=image", user.AccessToken));
        var imagePage = await images.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, imagePage.GetProperty("total").GetInt64());

        // Size sort ascending puts the smallest first.
        var sorted = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/files?sort=size&ascending=true", user.AccessToken));
        var sortedPage = await sorted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("notes.txt", sortedPage.GetProperty("items")[0].GetProperty("name").GetString());

        // Trash hides files from listings, restore brings them back.
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.SendAsync(Authorized(HttpMethod.Delete, $"/api/files/{second}", user.AccessToken))).StatusCode);

        var afterDelete = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/files", user.AccessToken));
        Assert.Equal(2, (await afterDelete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetInt64());

        var trash = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/trash", user.AccessToken));
        var trashPage = await trash.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, trashPage.GetProperty("total").GetInt64());
        Assert.Equal("photo.png", trashPage.GetProperty("items")[0].GetProperty("name").GetString());

        // Restore returns the restored file metadata.
        var restored = await Client.SendAsync(Authorized(HttpMethod.Post, $"/api/trash/{second}/restore", user.AccessToken));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal("photo.png", (await restored.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("name").GetString());

        var afterRestore = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/files", user.AccessToken));
        Assert.Equal(3, (await afterRestore.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetInt64());

        // Permanent delete removes it for good.
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.SendAsync(Authorized(HttpMethod.Delete, $"/api/files/{third}?permanent=true", user.AccessToken))).StatusCode);
        var finalList = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/files", user.AccessToken));
        Assert.Equal(2, (await finalList.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Folders_Create_Rename_Move_Delete_TrashFiles()
    {
        var user = await RegisterAsync("folderops");

        var create = await Client.SendAsync(Authorized(HttpMethod.Post, "/api/folders", user.AccessToken,
            JsonBody(new { name = "Projects" })));
        create.EnsureSuccessStatusCode();
        var folder = await create.Content.ReadFromJsonAsync<JsonElement>();
        var folderId = folder.GetProperty("id").GetString()!;

        // Duplicate sibling names are rejected.
        var duplicate = await Client.SendAsync(Authorized(HttpMethod.Post, "/api/folders", user.AccessToken,
            JsonBody(new { name = "Projects" })));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var subCreate = await Client.SendAsync(Authorized(HttpMethod.Post, "/api/folders", user.AccessToken,
            JsonBody(new { name = "2026", parentId = folderId })));
        var subfolder = await subCreate.Content.ReadFromJsonAsync<JsonElement>();
        var subfolderId = subfolder.GetProperty("id").GetString()!;

        // Upload a file directly into the subfolder.
        var fileId = await UploadSmallFileAsync(user.AccessToken, "inside.txt", "data"u8.ToArray());
        await Client.SendAsync(Authorized(HttpMethod.Post, $"/api/files/{fileId}/move", user.AccessToken,
            JsonBody(new { folderId = subfolderId })));

        var contents = await Client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/folders/contents?folderId={subfolderId}", user.AccessToken));
        var contentsBody = await contents.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, contentsBody.GetProperty("files").GetProperty("total").GetInt64());
        Assert.Equal(2, contentsBody.GetProperty("breadcrumbs").GetArrayLength());

        // Cycle prevention: parent cannot move into its own child.
        var cycle = await Client.SendAsync(Authorized(HttpMethod.Post, $"/api/folders/{folderId}/move",
            user.AccessToken, JsonBody(new { parentId = subfolderId })));
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);

        // Deleting the tree trashes contained files.
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.SendAsync(Authorized(HttpMethod.Delete, $"/api/folders/{folderId}", user.AccessToken))).StatusCode);

        var trash = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/trash", user.AccessToken));
        var trashBody = await trash.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, trashBody.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Quota_Enforced_Backend()
    {
        var admin = await LoginAsync("admin");
        var user = await RegisterAsync("quotauser");
        var userId = (await (await Client.SendAsync(Authorized(HttpMethod.Get, "/api/auth/me", user.AccessToken)))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        // Shrink the quota to 10 bytes.
        await Client.SendAsync(Authorized(HttpMethod.Put, $"/api/users/{userId}", admin.AccessToken,
            JsonBody(new { storageQuotaBytes = 10 })));

        var channel = GrpcChannel.ForAddress(Client.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = Factory.CreateClient(),
        });
        var client = new FileTransferClient(channel);
        var call = client.Upload(headers: new Metadata { { "Authorization", $"Bearer {user.AccessToken}" } });
        await call.RequestStream.WriteAsync(new UploadRequest
        {
            Init = new UploadInit { FileName = "toobig.txt", Size = 1000 },
        });
        await call.RequestStream.CompleteAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await call);
        Assert.Equal(StatusCode.ResourceExhausted, ex.StatusCode);
    }

    [Fact]
    public async Task UserDeletion_Cascades_FilesAndShares()
    {
        var admin = await LoginAsync("admin");
        var user = await RegisterAsync("disposable");
        var fileId = await UploadSmallFileAsync(user.AccessToken, "doomed.txt", "xx"u8.ToArray());
        await Client.SendAsync(Authorized(HttpMethod.Post, $"/api/files/{fileId}/share", user.AccessToken,
            JsonBody(new { })));

        var userId = (await (await Client.SendAsync(Authorized(HttpMethod.Get, "/api/auth/me", user.AccessToken)))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var delete = await Client.SendAsync(Authorized(HttpMethod.Delete, $"/api/users/{userId}", admin.AccessToken));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // The old access token no longer maps to an account.
        var filesAfter = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/files", user.AccessToken));
        Assert.Equal(HttpStatusCode.Forbidden, filesAfter.StatusCode);
    }

    [Fact]
    public async Task Dashboard_And_Activity_Reflect_Operations()
    {
        var user = await RegisterAsync("dashuser");
        await UploadSmallFileAsync(user.AccessToken, "dash.txt", "abc"u8.ToArray());

        var dashboard = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/dashboard", user.AccessToken));
        dashboard.EnsureSuccessStatusCode();
        var board = await dashboard.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, board.GetProperty("totalFiles").GetInt64());
        Assert.Equal(3, board.GetProperty("storageUsedBytes").GetInt64());

        var activity = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/activity", user.AccessToken));
        var activityBody = await activity.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(activityBody.GetProperty("total").GetInt64() >= 1);

        var admin = await LoginAsync("admin");
        var adminBoard = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/dashboard/admin", admin.AccessToken));
        adminBoard.EnsureSuccessStatusCode();
        var system = await adminBoard.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(system.GetProperty("totalUsers").GetInt64() >= 2);
    }
}
