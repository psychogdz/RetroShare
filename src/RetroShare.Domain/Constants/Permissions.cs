namespace RetroShare.Domain.Constants;

/// <summary>The catalog of every permission known to the system. Permission names double as
/// ASP.NET Core authorization policy names (see the policy registration in the API layer).</summary>
public static class Permissions
{
    public const string FilesView = "files.view";
    public const string FilesUpload = "files.upload";
    public const string FilesDownload = "files.download";
    public const string FilesRename = "files.rename";
    public const string FilesDelete = "files.delete";
    public const string FilesRestore = "files.restore";

    public const string FoldersView = "folders.view";
    public const string FoldersCreate = "folders.create";
    public const string FoldersRename = "folders.rename";
    public const string FoldersDelete = "folders.delete";

    public const string SharesView = "shares.view";
    public const string SharesCreate = "shares.create";
    public const string SharesDelete = "shares.delete";

    public const string ProfileView = "profile.view";
    public const string ProfileUpdate = "profile.update";

    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersDelete = "users.delete";

    public const string RolesView = "roles.view";
    public const string RolesCreate = "roles.create";
    public const string RolesUpdate = "roles.update";
    public const string RolesDelete = "roles.delete";

    public const string PermissionsView = "permissions.view";

    public const string SystemManage = "system.manage";

    /// <summary>All permissions with display metadata, used for database seeding and the
    /// admin UI. The list is the single source of truth — nothing else defines permissions.</summary>
    public static readonly (string Name, string Category, string Description)[] All =
    [
        (FilesView, "files", "View own files and metadata"),
        (FilesUpload, "files", "Upload files via gRPC streaming"),
        (FilesDownload, "files", "Download own files via gRPC streaming"),
        (FilesRename, "files", "Rename own files"),
        (FilesDelete, "files", "Move own files to trash"),
        (FilesRestore, "files", "Restore files from trash"),

        (FoldersView, "folders", "View folders"),
        (FoldersCreate, "folders", "Create folders"),
        (FoldersRename, "folders", "Rename folders"),
        (FoldersDelete, "folders", "Delete folders"),

        (SharesView, "shares", "View own share links"),
        (SharesCreate, "shares", "Create share links"),
        (SharesDelete, "shares", "Revoke share links"),

        (ProfileView, "profile", "View own profile"),
        (ProfileUpdate, "profile", "Update own profile and password"),

        (UsersView, "users", "View all users"),
        (UsersCreate, "users", "Create users"),
        (UsersUpdate, "users", "Update users, roles and quotas"),
        (UsersDelete, "users", "Delete users"),

        (RolesView, "roles", "View roles"),
        (RolesCreate, "roles", "Create roles"),
        (RolesUpdate, "roles", "Update roles and assign permissions"),
        (RolesDelete, "roles", "Delete roles"),

        (PermissionsView, "permissions", "View the permission catalog"),

        (SystemManage, "system", "Manage system configuration and statistics"),
    ];

    /// <summary>Permissions granted to the seeded baseline "User" role.</summary>
    public static readonly string[] UserRole =
    [
        FilesView, FilesUpload, FilesDownload, FilesRename, FilesDelete, FilesRestore,
        FoldersView, FoldersCreate, FoldersRename, FoldersDelete,
        SharesView, SharesCreate, SharesDelete,
        ProfileView, ProfileUpdate,
    ];

    /// <summary>Permissions granted to the seeded "Moderator" role: user capabilities plus
    /// moderation of all files/shares and read access to users.</summary>
    public static readonly string[] ModeratorRole =
    [
        ..UserRole,
        UsersView, PermissionsView,
    ];

    /// <summary>Permissions granted to the seeded "Admin" role: everything.</summary>
    public static string[] AdminRole => [..All.Select(p => p.Name)];
}
