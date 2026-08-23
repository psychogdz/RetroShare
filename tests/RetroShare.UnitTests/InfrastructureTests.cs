using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetroShare.Application.Common;
using RetroShare.Infrastructure.Security;
using RetroShare.Infrastructure.Storage;
using RetroShare.Domain.Entities;
using Xunit;

namespace RetroShare.UnitTests;

public class JwtTokenServiceTests
{
    private readonly JwtTokenService _service = new(Options.Create(new JwtOptions
    {
        Secret = "unit-test-secret-key-with-enough-bytes-1234",
        Issuer = "RetroShare",
        Audience = "RetroShare",
        AccessTokenMinutes = 15,
    }));

    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        Username = "tester",
        Email = "tester@example.com",
    };

    [Fact]
    public void Token_Carries_Identity_Roles_AndPermissions()
    {
        var (token, expires) = _service.IssueAccessToken(
            NewUser(), ["Admin"], ["files.upload", "system.manage"]);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("tester", jwt.Claims.First(c => c.Type == "username").Value);
        Assert.Contains(jwt.Claims, c => c.Value == "Admin"
            && c.Type is "role" or ClaimTypes.Role);
        Assert.Contains(jwt.Claims, c => c.Type == "perm" && c.Value == "files.upload");
        Assert.True(expires > DateTime.UtcNow.AddMinutes(14));
    }

    [Fact]
    public void Refuses_ToSign_WithWeakSecret()
    {
        var weak = new JwtTokenService(Options.Create(new JwtOptions { Secret = "short" }));
        Assert.Throws<InvalidOperationException>(
            () => weak.IssueAccessToken(NewUser(), [], []));
    }
}

public class LocalFileStoragePathTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileStorage _storage;

    public LocalFileStoragePathTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"rs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _storage = new LocalFileStorage(
            Options.Create(new StorageOptions { Root = _root }),
            NullLogger<LocalFileStorage>.Instance);
    }

    [Fact]
    public async Task Write_And_Read_Roundtrip()
    {
        var path = _storage.BuildRelativePath(Guid.NewGuid(), Guid.NewGuid());
        var payload = "retroshare"u8.ToArray();

        await using (var stream = await _storage.OpenWriteAsync(path))
        {
            await stream.WriteAsync(payload);
        }

        await using var read = await _storage.OpenReadAsync(path);
        using var memory = new MemoryStream();
        await read.CopyToAsync(memory);
        Assert.Equal(payload, memory.ToArray());
        Assert.Equal(payload.Length, await _storage.GetSizeAsync(path));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config")]
    [InlineData("users/../../secrets")]
    public async Task Rejects_PathTraversal(string relativePath)
    {
        // A backslash only separates path components on Windows; on Unix the
        // payload stays inside the root and the guard rightly accepts it.
        if (!OperatingSystem.IsWindows() && relativePath.Contains('\\'))
        {
            return;
        }

        await Assert.ThrowsAnyAsync<Exception>(() => _storage.OpenWriteAsync(relativePath));
    }

    [Fact]
    public async Task Delete_RemovesBlob_AndReportsMissing()
    {
        var path = _storage.BuildRelativePath(Guid.NewGuid(), Guid.NewGuid());
        await using (var stream = await _storage.OpenWriteAsync(path))
        {
            await stream.WriteAsync(new byte[] { 1, 2, 3 });
        }

        await _storage.DeleteAsync(path);
        Assert.False(await _storage.ExistsAsync(path));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp cleanup best-effort */ }
    }
}
