using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RetroShare.Application.Common;
using RetroShare.Application.Interfaces;
using RetroShare.Infrastructure.Data;
using RetroShare.Infrastructure.Security;
using RetroShare.Infrastructure.Storage;

namespace RetroShare.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers persistence, storage and security implementations, plus the
    /// configuration-bound option objects.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.AccessTokenMinutes is > 0 and <= 24 * 60, "Access token lifetime must be 1..1440 minutes.")
            .Validate(o => o.RefreshTokenDays is > 0 and <= 365, "Refresh token lifetime must be 1..365 days.")
            .ValidateOnStart();

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .PostConfigure(o =>
            {
                // Resolve relative storage roots against the app content root so the working
                // directory never matters.
                if (!Path.IsPathRooted(o.Root))
                {
                    o.Root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, o.Root));
                }
            });

        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName));

        services.AddDbContext<AppDbContext>(options =>
        {
            // Resolve a relative SQLite data source against the content root so the database
            // location does not depend on the current working directory.
            var raw = configuration.GetConnectionString("Database") ?? "Data Source=retroshare.db";
            var builder = new SqliteConnectionStringBuilder(raw);
            if (!string.IsNullOrEmpty(builder.DataSource) && !Path.IsPathRooted(builder.DataSource))
            {
                builder.DataSource = Path.Combine(environment.ContentRootPath, builder.DataSource);
            }

            options.UseSqlite(builder.ToString(), sqlite => sqlite.CommandTimeout(30));
#if DEBUG
            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging(false);
            }
#endif
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IShareRepository, ShareRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IActivityRepository, ActivityRepository>();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddMemoryCache();
        services.AddScoped<IPermissionChecker, PermissionChecker>();

        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<DbSeeder>();

        return services;
    }
}
