using System.ComponentModel.DataAnnotations;
using RetroShare.Domain.Constants;

namespace RetroShare.Application.DTOs;

public class RegisterRequest
{
    [Required, MinLength(3), MaxLength(32), RegularExpression(@"^[a-zA-Z0-9_.-]+$",
        ErrorMessage = "Username may only contain letters, digits, '.', '_' and '-'.")]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? DisplayName { get; set; }
}

public class LoginRequest
{
    /// <summary>Accepts either the username or the email address.</summary>
    [Required, MinLength(3), MaxLength(256)]
    public string Login { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(128)]
    public string Password { get; set; } = string.Empty;
}

public class RefreshRequest
{
    [Required, MinLength(10), MaxLength(512)]
    public string RefreshToken { get; set; } = string.Empty;
}

public class LogoutRequest
{
    [Required, MinLength(10), MaxLength(512)]
    public string RefreshToken { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    [Required, MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}

public class UpdateProfileRequest
{
    [Required, MinLength(1), MaxLength(64)]
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class AuthResponse
{
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTime AccessTokenExpiresAtUtc { get; init; }

    public required UserDto User { get; init; }
}

public sealed class UserDto
{
    public required Guid Id { get; init; }

    public required string Username { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public required DateTime CreatedAt { get; init; }

    public DateTime? LastLoginAt { get; init; }

    public bool IsDisabled { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }

    /// <summary>Effective permissions; populated for the authenticated user's own profile.</summary>
    public IReadOnlyList<string>? Permissions { get; init; }

    public long StorageQuotaBytes { get; init; }

    public long StorageUsedBytes { get; init; }
}
