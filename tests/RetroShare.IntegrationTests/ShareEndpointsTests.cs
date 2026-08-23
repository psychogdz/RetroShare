using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using RetroShare.Infrastructure.Grpc;
using Xunit;
using FileTransferClient = RetroShare.Infrastructure.Grpc.FileTransfer.FileTransferClient;

namespace RetroShare.IntegrationTests;

public class ShareEndpointsTests(RetroShareFactory factory) : ApiTestBase(factory)
{
    private async Task<(string Token, string FileId)> UploadAsync(string username)
    {
        var auth = await RegisterAsync(username);
        var channel = GrpcChannel.ForAddress(Client.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = Factory.CreateClient(),
        });
        var client = new FileTransferClient(channel);
        var call = client.Upload(headers: new Metadata { { "Authorization", $"Bearer {auth.AccessToken}" } });
        await call.RequestStream.WriteAsync(new UploadRequest
        {
            Init = new UploadInit { FileName = "shareable.txt", Size = 6, MimeType = "text/plain" },
        });
        await call.RequestStream.WriteAsync(new UploadRequest
        {
            Chunk = Google.Protobuf.ByteString.CopyFrom("shared"u8),
        });
        await call.RequestStream.CompleteAsync();
        return (auth.AccessToken, (await call).FileId);
    }

    private async Task<byte[]> DownloadShareAsync(string shareToken, string? password)
    {
        var channel = GrpcChannel.ForAddress(Client.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = Factory.CreateClient(),
        });
        var client = new FileTransferClient(channel);
        var call = client.Download(new DownloadRequest
        {
            ShareToken = shareToken,
            SharePassword = password ?? string.Empty,
        });

        var buffer = new MemoryStream();
        await foreach (var message in call.ResponseStream.ReadAllAsync())
        {
            if (message.Meta != null) continue;
            if (!message.Chunk.IsEmpty) message.Chunk.WriteTo(buffer);
        }

        return buffer.ToArray();
    }

    private async Task<JsonElement> CreateShareAsync(string accessToken, string fileId, object body)
    {
        var response = await Client.SendAsync(Authorized(HttpMethod.Post, $"/api/files/{fileId}/share",
            accessToken, JsonBody(body)));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task PublicShare_FullLifecycle()
    {
        var (token, fileId) = await UploadAsync("shareuser1");
        var share = await CreateShareAsync(token, fileId, new { });

        var shareToken = share.GetProperty("token").GetString()!;

        // Public info requires no authentication.
        var info = await Client.GetAsync($"/api/shares/{shareToken}");
        info.EnsureSuccessStatusCode();
        var infoBody = await info.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("shareable.txt", infoBody.GetProperty("fileName").GetString());
        Assert.False(infoBody.GetProperty("requiresPassword").GetBoolean());

        // Anonymous gRPC download works and returns the bytes.
        var bytes = await DownloadShareAsync(shareToken, null);
        Assert.Equal("shared"u8.ToArray(), bytes);

        // Owner sees it in their list.
        var own = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/shares", token));
        var ownBody = await own.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, ownBody.GetProperty("total").GetInt64());

        // Revocation blocks further downloads.
        var shareId = share.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.SendAsync(Authorized(HttpMethod.Delete, $"/api/shares/{shareId}", token))).StatusCode);

        var revokedInfo = await Client.GetAsync($"/api/shares/{shareToken}");
        Assert.Equal(HttpStatusCode.OK, revokedInfo.StatusCode); // info still describes the link
        var revokedBody = await revokedInfo.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(revokedBody.GetProperty("isAvailable").GetBoolean());

        await Assert.ThrowsAsync<RpcException>(async () => await DownloadShareAsync(shareToken, null));
    }

    [Fact]
    public async Task PasswordProtectedShare_Requires_CorrectPassword()
    {
        var (token, fileId) = await UploadAsync("shareuser2");
        var share = await CreateShareAsync(token, fileId, new { password = "letmein" });
        var shareToken = share.GetProperty("token").GetString()!;

        var info = await Client.GetAsync($"/api/shares/{shareToken}");
        var infoBody = await info.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(infoBody.GetProperty("requiresPassword").GetBoolean());

        var noPassword = await Assert.ThrowsAsync<RpcException>(() => DownloadShareAsync(shareToken, null));
        Assert.Equal(StatusCode.Unauthenticated, noPassword.StatusCode);

        var wrongPassword = await Assert.ThrowsAsync<RpcException>(() => DownloadShareAsync(shareToken, "wrong"));
        Assert.Equal(StatusCode.Unauthenticated, wrongPassword.StatusCode);

        var bytes = await DownloadShareAsync(shareToken, "letmein");
        Assert.Equal("shared"u8.ToArray(), bytes);
    }

    [Fact]
    public async Task DownloadLimit_Stops_After_MaxDownloads()
    {
        var (token, fileId) = await UploadAsync("shareuser3");
        var share = await CreateShareAsync(token, fileId, new { maxDownloads = 1 });
        var shareToken = share.GetProperty("token").GetString()!;

        var first = await DownloadShareAsync(shareToken, null);
        Assert.Equal("shared"u8.ToArray(), first);

        var second = await Assert.ThrowsAsync<RpcException>(() => DownloadShareAsync(shareToken, null));
        Assert.Equal(StatusCode.FailedPrecondition, second.StatusCode);
    }

    [Fact]
    public async Task Expiry_InPast_Rejected()
    {
        var (token, fileId) = await UploadAsync("shareuser4");
        var response = await Client.SendAsync(Authorized(HttpMethod.Post, $"/api/files/{fileId}/share",
            token, JsonBody(new { expiresAt = DateTime.UtcNow.AddDays(-1) })));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Share_Of_Trashed_File_Is_Unusable()
    {
        var (token, fileId) = await UploadAsync("shareuser5");
        var share = await CreateShareAsync(token, fileId, new { });
        var shareToken = share.GetProperty("token").GetString()!;

        await Client.SendAsync(Authorized(HttpMethod.Delete, $"/api/files/{fileId}", token));

        var info = await Client.GetAsync($"/api/shares/{shareToken}");
        Assert.Equal(HttpStatusCode.Forbidden, info.StatusCode);
    }

    [Fact]
    public async Task Admin_Can_List_And_Revoke_Any_Share()
    {
        var (ownerToken, fileId) = await UploadAsync("shareuser6");
        var share = await CreateShareAsync(ownerToken, fileId, new { });
        var shareId = share.GetProperty("id").GetString()!;

        var admin = await LoginAsync("admin");
        var list = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/shares/all", admin.AccessToken));
        list.EnsureSuccessStatusCode();
        var page = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(page.GetProperty("total").GetInt64() >= 1);

        var revoke = await Client.SendAsync(Authorized(HttpMethod.Delete, $"/api/shares/{shareId}", admin.AccessToken));
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
    }

    [Fact]
    public async Task Stranger_Cannot_Share_Someone_Elses_File()
    {
        var (ownerToken, fileId) = await UploadAsync("shareuser7");
        var stranger = await RegisterAsync("sharestranger7");

        var response = await Client.SendAsync(Authorized(HttpMethod.Post, $"/api/files/{fileId}/share",
            stranger.AccessToken, JsonBody(new { })));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
