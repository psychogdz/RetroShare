using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetroShare.Infrastructure.Data;

namespace RetroShare.IntegrationTests;

/// <summary>Boots the full application (REST + gRPC + frontend) against an in-memory SQLite
/// database with a kept-open connection and a temporary storage root. Each factory uses a
/// uniquely named shared-cache in-memory database so concurrent factories never share
/// state (SQLite connection pooling would otherwise hand the same in-memory DB around).</summary>
public sealed class RetroShareFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection =
        new($"Data Source=file:rsit_{Guid.NewGuid():N}?mode=memory&cache=shared");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "integration-test-secret-that-is-long-enough-1234",
            ["Jwt:AccessTokenMinutes"] = "15",
            ["RateLimit:Enabled"] = "false",
            ["Storage:Root"] = Path.Combine(Path.GetTempPath(), $"rs-it-{Guid.NewGuid():N}"),
            ["Storage:DefaultUserQuotaBytes"] = "104857600", // 100 MB for quota tests
        }));

        builder.ConfigureServices(services =>
        {
            // Replace the file-based SQLite context with the shared in-memory connection.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
