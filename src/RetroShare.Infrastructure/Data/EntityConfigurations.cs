using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetroShare.Domain.Entities;

namespace RetroShare.Infrastructure.Data;

/// <summary>All EF mappings in one place: keys, indexes, delete behaviors and value
/// conversions. Delete behaviors avoid cascade cycles (self-references and cross-aggregate
/// FKs are Restrict and handled by the Application services).</summary>
public static class EntityConfigurations
{
    public sealed class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.HasIndex(u => u.Username).IsUnique();
            builder.HasIndex(u => u.Email).IsUnique();
            builder.Property(u => u.Username).HasMaxLength(64);
            builder.Property(u => u.Email).HasMaxLength(256);
            builder.Property(u => u.DisplayName).HasMaxLength(64);
            builder.Property(u => u.StorageQuotaBytes);
        }
    }

    public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
    {
        public void Configure(EntityTypeBuilder<Role> builder)
        {
            builder.HasIndex(r => r.Name).IsUnique();
            builder.Property(r => r.Name).HasMaxLength(64);
            builder.Property(r => r.Description).HasMaxLength(256);
        }
    }

    public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
    {
        public void Configure(EntityTypeBuilder<Permission> builder)
        {
            builder.HasIndex(p => p.Name).IsUnique();
            builder.Property(p => p.Name).HasMaxLength(64);
            builder.Property(p => p.Category).HasMaxLength(32);
            builder.Property(p => p.Description).HasMaxLength(256);
        }
    }

    public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
    {
        public void Configure(EntityTypeBuilder<UserRole> builder)
        {
            builder.HasKey(ur => new { ur.UserId, ur.RoleId });
            builder.HasOne(ur => ur.User)
                .WithMany(u => u.Roles)
                .HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(ur => ur.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
    {
        public void Configure(EntityTypeBuilder<RolePermission> builder)
        {
            builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });
            builder.HasOne(rp => rp.Role)
                .WithMany(r => r.Permissions)
                .HasForeignKey(rp => rp.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(rp => rp.Permission)
                .WithMany(p => p.Roles)
                .HasForeignKey(rp => rp.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
    {
        public void Configure(EntityTypeBuilder<RefreshToken> builder)
        {
            builder.HasIndex(rt => rt.TokenHash).IsUnique();
            builder.HasIndex(rt => rt.UserId);
            builder.Property(rt => rt.TokenHash).HasMaxLength(128);
            builder.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
    {
        public void Configure(EntityTypeBuilder<StoredFile> builder)
        {
            builder.HasIndex(f => f.OwnerId);
            builder.HasIndex(f => f.Name);
            builder.HasIndex(f => new { f.OwnerId, f.FolderId });
            builder.HasIndex(f => f.DeletedAt);
            builder.Property(f => f.Name).HasMaxLength(255);
            builder.Property(f => f.StoredName).HasMaxLength(128);
            builder.Property(f => f.StoragePath).HasMaxLength(512);
            builder.Property(f => f.MimeType).HasMaxLength(128);
            builder.Property(f => f.Extension).HasMaxLength(32);
            builder.HasOne(f => f.Owner)
                .WithMany(u => u.Files)
                .HasForeignKey(f => f.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(f => f.Folder)
                .WithMany(fo => fo.Files)
                .HasForeignKey(f => f.FolderId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
    {
        public void Configure(EntityTypeBuilder<Folder> builder)
        {
            builder.HasIndex(f => f.OwnerId);
            builder.HasIndex(f => new { f.OwnerId, f.ParentId });
            builder.Property(f => f.Name).HasMaxLength(255);
            builder.HasOne(f => f.Owner)
                .WithMany(u => u.Folders)
                .HasForeignKey(f => f.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(f => f.Parent)
                .WithMany(f => f.Children)
                .HasForeignKey(f => f.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class ShareLinkConfiguration : IEntityTypeConfiguration<ShareLink>
    {
        public void Configure(EntityTypeBuilder<ShareLink> builder)
        {
            builder.HasIndex(s => s.Token).IsUnique();
            builder.HasIndex(s => s.ExpiresAt);
            builder.HasIndex(s => s.FileId);
            builder.Property(s => s.Token).HasMaxLength(64);
            builder.Property(s => s.PasswordHash).HasMaxLength(512);
            builder.HasOne(s => s.File)
                .WithMany(f => f.Shares)
                .HasForeignKey(s => s.FileId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<User>()
                .WithMany(u => u.ShareLinks)
                .HasForeignKey(s => s.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
    {
        public void Configure(EntityTypeBuilder<ActivityLog> builder)
        {
            builder.HasIndex(a => a.CreatedAt);
            builder.HasIndex(a => a.UserId);
            builder.HasIndex(a => a.Action);
            builder.Property(a => a.Description).HasMaxLength(512);
            builder.Property(a => a.EntityType).HasMaxLength(64);
            builder.Property(a => a.EntityId).HasMaxLength(64);
            builder.Property(a => a.IpAddress).HasMaxLength(64);
            builder.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
