namespace RetroShare.Domain.Enums;

/// <summary>Auditable actions tracked in the activity log.</summary>
public enum ActivityAction
{
    UserRegistered = 1,
    UserLoggedIn = 2,
    UserLoggedOut = 3,
    UserLoginFailed = 4,
    UserDisabled = 5,
    UserEnabled = 6,
    UserRolesChanged = 7,
    PasswordChanged = 8,
    QuotaChanged = 9,
    FileUploaded = 10,
    FileDownloaded = 11,
    FileRenamed = 12,
    FileMoved = 13,
    FileDeleted = 14,
    FileRestored = 15,
    FilePermanentlyDeleted = 16,
    FolderCreated = 17,
    FolderRenamed = 18,
    FolderDeleted = 19,
    ShareCreated = 20,
    ShareRevoked = 21,
    ShareDownloaded = 22,
    RoleCreated = 23,
    RoleUpdated = 24,
    RoleDeleted = 25,
    PermissionDenied = 26,
    UserDeleted = 27
}
