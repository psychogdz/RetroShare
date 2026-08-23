using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetroShare.Application.Common;
using RetroShare.Application.Interfaces;
using RetroShare.Domain.Constants;
using RetroShare.Domain.Entities;

namespace RetroShare.Infrastructure.Data;

/// <summary>Idempotent database seeding: permission catalog, the three system roles with
/// their default permission sets, and the bootstrap administrator. Safe to run on every
/// startup — it only adds what is missing and never revokes customizations.</summary>
public sealed class DbSeeder(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IOptions<SeedOptions> seedOptions,
    ILogger<DbSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedPermissionsAsync(ct);
        await db.SaveChangesAsync(ct); // permissions must exist before role joins reference them

        await SeedRolesAsync(ct);
        await db.SaveChangesAsync(ct); // roles must exist before admin assignment queries them

        await SeedAdminAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await db.Permissions.ToListAsync(ct);
        var byName = existing.ToDictionary(p => p.Name);

        foreach (var (name, category, description) in Permissions.All)
        {
            if (byName.TryGetValue(name, out var permission))
            {
                permission.Category = category;
                permission.Description = description;
                continue;
            }

            db.Permissions.Add(new Permission { Name = name, Category = category, Description = description });
            logger.LogInformation("Seeded permission {Permission}", name);
        }
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var permissions = await db.Permissions.ToDictionaryAsync(p => p.Name, ct);
        var roles = await db.Roles
            .Include(r => r.Permissions)
            .ToListAsync(ct);

        foreach (var (roleName, permissionNames, description) in new[]
        {
            (RoleNames.User, Permissions.UserRole, "Standard file-sharing capabilities."),
            (RoleNames.Moderator, Permissions.ModeratorRole, "Moderation access across users, files and shares."),
            (RoleNames.Admin, Permissions.AdminRole, "Full system access."),
        })
        {
            var role = roles.FirstOrDefault(r => r.Name == roleName);
            if (role is null)
            {
                role = new Role { Name = roleName, Description = description, IsSystem = true };
                db.Roles.Add(role);
                roles.Add(role);
                logger.LogInformation("Seeded role {Role}", roleName);
            }
            else
            {
                role.IsSystem = true;
                role.Description = description;
            }

            var granted = role.Permissions.Select(rp => rp.Permission.Name).ToHashSet();
            foreach (var permissionName in permissionNames)
            {
                if (granted.Contains(permissionName) || !permissions.TryGetValue(permissionName, out var permission))
                {
                    continue;
                }

                role.Permissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }
        }
    }

    private async Task SeedAdminAsync(CancellationToken ct)
    {
        var options = seedOptions.Value;
        var admin = await db.Users
            .Include(u => u.Roles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == options.AdminUsername.ToLowerInvariant(), ct);

        var adminRole = await db.Roles.FirstAsync(r => r.Name == RoleNames.Admin, ct);
        var defaultRole = await db.Roles.FirstAsync(r => r.Name == RoleNames.User, ct);

        if (admin is null)
        {
            admin = new User
            {
                Username = options.AdminUsername.ToLowerInvariant(),
                Email = options.AdminEmail.ToLowerInvariant(),
                DisplayName = "Administrator",
                PasswordHash = passwordHasher.Hash(options.AdminPassword),
                StorageQuotaBytes = long.MaxValue / 4, // effectively unlimited, still finite
            };
            admin.Roles.Add(new UserRole { RoleId = adminRole.Id });
            admin.Roles.Add(new UserRole { RoleId = defaultRole.Id });
            db.Users.Add(admin);
            logger.LogInformation("Seeded administrator account {Username}", options.AdminUsername);
        }
        else if (admin.Roles.All(ur => ur.Role.Name != RoleNames.Admin))
        {
            admin.Roles.Add(new UserRole { RoleId = adminRole.Id });
        }
    }
}

/// <summary>Applies migrations and seeds reference data at startup.</summary>
public sealed class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DbSeeder>>();

        logger.LogInformation("Applying database migrations");
        await db.Database.MigrateAsync(ct);

        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        await seeder.SeedAsync(ct);
        logger.LogInformation("Database ready");
    }
}
