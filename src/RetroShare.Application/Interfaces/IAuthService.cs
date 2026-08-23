using RetroShare.Application.DTOs;

using RetroShare.Application.Common;

namespace RetroShare.Application.Interfaces;

public interface IAuthService
{
    /// <summary>Creates a new account with the default role and returns an initial token pair.</summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default);

    /// <summary>Validates credentials and returns a fresh token pair.</summary>
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default);

    /// <summary>Rotates a valid refresh token: the presented token is revoked and replaced.</summary>
    Task<AuthResponse> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken ct = default);

    /// <summary>Revokes the presented refresh token.</summary>
    Task LogoutAsync(string refreshToken, string? ipAddress, CancellationToken ct = default);

    Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default);
}

public interface IProfileService
{
    Task<UserDto> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct = default);

    Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword,
        string? ipAddress = null, CancellationToken ct = default);
}

public interface IUserManagementService
{
    Task<PagedResult<UserDto>> ListAsync(string? search, bool? disabled, string sort, bool ascending,
        int page, int pageSize, CancellationToken ct = default);

    Task<UserDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<UserDto> UpdateAsync(Guid id, AdminUpdateUserRequest request, Guid actorId, CancellationToken ct = default);

    Task<UserDto> SetRolesAsync(Guid id, IReadOnlyCollection<int> roleIds, Guid actorId, CancellationToken ct = default);

    /// <summary>Deletes a user and all owned data, including physical file blobs.</summary>
    Task DeleteAsync(Guid id, Guid actorId, CancellationToken ct = default);
}
