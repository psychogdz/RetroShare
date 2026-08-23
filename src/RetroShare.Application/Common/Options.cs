namespace RetroShare.Application.Common;

/// <summary>JWT configuration bound from the "Jwt" configuration section (env-overridable).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Base64 or plain secret used to sign access tokens. Must be configured in production.</summary>
    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "RetroShare";

    public string Audience { get; set; } = "RetroShare";

    /// <summary>Access token lifetime in minutes. Kept short; refresh tokens extend sessions.</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh token lifetime in days.</summary>
    public int RefreshTokenDays { get; set; } = 7;
}

/// <summary>Storage configuration bound from the "Storage" section.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Directory root for physical file blobs. Relative paths resolve against app content root.</summary>
    public string Root { get; set; } = "storage";

    /// <summary>Maximum size of a single uploaded file, in bytes.</summary>
    public long MaxFileSizeBytes { get; set; } = Domain.Constants.FileRules.DefaultMaxFileSizeBytes;

    /// <summary>Quota assigned to newly registered users, in bytes.</summary>
    public long DefaultUserQuotaBytes { get; set; } = Domain.Constants.FileRules.DefaultUserQuotaBytes;

    /// <summary>Seconds a completed-but-uncommitted upload session may live before cleanup.</summary>
    public int UploadSessionTimeoutSeconds { get; set; } = 600;
}

/// <summary>Bootstrap/administration configuration bound from the "Seed" section.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminUsername { get; set; } = "admin";

    public string AdminEmail { get; set; } = "admin@retroshare.local";

    /// <summary>Dev-only default password; the API refuses to start with it in production.</summary>
    public string AdminPassword { get; set; } = "ChangeMe!123";
}
