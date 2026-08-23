namespace RetroShare.Domain.Constants;

/// <summary>Names of the seeded roles. Used only for seeding and the registration default —
/// never for capability checks, which go through permissions.</summary>
public static class RoleNames
{
    public const string User = "User";
    public const string Moderator = "Moderator";
    public const string Admin = "Admin";
}
