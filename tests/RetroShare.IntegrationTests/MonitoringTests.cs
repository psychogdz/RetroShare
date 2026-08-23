using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;
using FileTransferClient = RetroShare.Infrastructure.Grpc.FileTransfer.FileTransferClient;

namespace RetroShare.IntegrationTests;

/// <summary>Server monitoring: permission enforcement on the endpoint and DTO sanity.</summary>
public class SystemMonitoringTests(RetroShareFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Monitor_Unauthenticated_IsRejected()
    {
        var response = await Client.GetAsync("/api/system/monitor");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Monitor_UserWithoutPermission_IsForbidden()
    {
        var user = await RegisterAsync($"plain{Guid.NewGuid():N}"[..20]);

        var response = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/system/monitor", user.AccessToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Monitor_Admin_ReturnsCoherentSnapshot()
    {
        var admin = await LoginAsync("admin");

        var response = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/system/monitor", admin.AccessToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        var diskTotal = payload.GetProperty("diskTotalBytes").GetInt64();
        var diskFree = payload.GetProperty("diskFreeBytes").GetInt64();
        var diskUsed = payload.GetProperty("diskUsedBytes").GetInt64();
        var diskPercent = payload.GetProperty("diskUsagePercent").GetDouble();

        Assert.True(diskTotal > 0, "disk total must be positive");
        Assert.True(diskFree > 0, "disk free must be positive");
        Assert.True(diskUsed >= 0 && diskUsed <= diskTotal, "used must be within total");
        Assert.InRange(diskPercent, 0, 100);

        var state = payload.GetProperty("diskState").GetString();
        Assert.Contains(state, new[] { "Healthy", "Warning", "Critical" });

        // Configured thresholds are echoed for the UI.
        Assert.Equal(80, payload.GetProperty("diskWarningThresholdPercent").GetInt32());
        Assert.Equal(90, payload.GetProperty("diskCriticalThresholdPercent").GetInt32());

        Assert.True(payload.GetProperty("fileCount").GetInt64() >= 0);
        Assert.True(payload.GetProperty("storedBytes").GetInt64() >= 0);
        Assert.True(payload.GetProperty("uptimeSeconds").GetInt64() > 0);
    }

    [Fact]
    public async Task Monitor_StorageDirectorySize_MatchesUploadedBytes()
    {
        var user = await RegisterAsync($"stor{Guid.NewGuid():N}"[..20]);
        var payload = new byte[4096];

        var channel = Grpc.Net.Client.GrpcChannel.ForAddress(Client.BaseAddress!,
            new Grpc.Net.Client.GrpcChannelOptions { HttpClient = Factory.CreateClient() });
        var grpc = new FileTransferClient(channel);
        await Upload(grpc, user.AccessToken, "monitor-size.bin", payload);

        var response = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/system/monitor",
            (await LoginAsync("admin")).AccessToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var dirSize = body.GetProperty("storageDirectoryBytes").GetInt64();
        Assert.True(dirSize >= payload.Length, $"directory size {dirSize} should cover the uploaded blob");
    }

    private static async Task Upload(FileTransferClient client, string token, string name, byte[] payload)
    {
        using var call = client.Upload(new Metadata { { "Authorization", $"Bearer {token}" } });
        await call.RequestStream.WriteAsync(new RetroShare.Infrastructure.Grpc.UploadRequest
        {
            Init = new RetroShare.Infrastructure.Grpc.UploadInit
            {
                FileName = name,
                Size = payload.Length,
                MimeType = "application/octet-stream",
            },
        });
        await call.RequestStream.WriteAsync(new RetroShare.Infrastructure.Grpc.UploadRequest { Chunk = Google.Protobuf.ByteString.CopyFrom(payload) });
        await call.RequestStream.CompleteAsync();
        await call;
    }
}

/// <summary>Factory with an absurd disk reserve so every upload trips the low-disk guard.</summary>
public sealed class LowDiskFactory : RetroShareFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:DiskReserveBytes"] = long.MaxValue.ToString(), // free space can never cover the reserve
        }));
    }
}

/// <summary>Low-disk protection: uploads are rejected cleanly before any bytes land.</summary>
public class LowDiskUploadTests(LowDiskFactory factory) : IClassFixture<LowDiskFactory>
{
    private readonly LowDiskFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Upload_InsufficientDisk_IsRejectedCleanly()
    {
        var login = await _client.PostAsync("/api/auth/login",
            new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { login = "admin", password = "ChangeMe!123" }),
                null, "application/json"));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var channel = Grpc.Net.Client.GrpcChannel.ForAddress(_client.BaseAddress!,
            new Grpc.Net.Client.GrpcChannelOptions { HttpClient = _factory.CreateClient() });
        var grpc = new FileTransferClient(channel);

        using var call = grpc.Upload(new Metadata { { "Authorization", $"Bearer {token}" } });
        await call.RequestStream.WriteAsync(new RetroShare.Infrastructure.Grpc.UploadRequest
        {
            Init = new RetroShare.Infrastructure.Grpc.UploadInit
            {
                FileName = "should-not-land.bin",
                Size = 1024,
                MimeType = "application/octet-stream",
            },
        });
        await call.RequestStream.WriteAsync(new RetroShare.Infrastructure.Grpc.UploadRequest
        {
            Chunk = Google.Protobuf.ByteString.CopyFrom(new byte[1024]),
        });
        await call.RequestStream.CompleteAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
        Assert.Equal(StatusCode.ResourceExhausted, ex.StatusCode);
        Assert.Equal("Insufficient storage space available.", ex.Status.Detail);
    }
}
