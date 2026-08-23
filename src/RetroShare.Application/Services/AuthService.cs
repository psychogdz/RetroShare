using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Application.Mapping;
using RetroShare.Application.Validation;
using RetroShare.Domain.Constants;
using RetroShare.Domain.Entities;
using RetroShare.Domain.Enums;

namespace RetroShare.Application.Services;

/// <summary>Registration, login, refresh-token rotation and logout.</summary>
public sealed class AuthService(
    IUserRepository users,
    IRoleRepository roles,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    ISecureTokenGenerator tokenGenerator,
    IJwtTokenService jwtService,
    IPermissionChecker permissionChecker,
    IUnitOfWork unitOfWork,
    IActivityLogger activity,
    IOptions<JwtOptions> jwtOptions,
    IOptions<StorageOptions> storageOptions,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly StorageOptions _storage = storageOptions.Value;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default)
    {
        if (!Validators.IsValidUsername(request.Username, out var usernameError))
        {
            throw new ValidationException(usernameError,
                new Dictionary<string, string[]> { [nameof(request.Username)] = [usernameError] });
        }

        if (!Validators.IsValidEmail(request.Email, out var emailError))
        {
            throw new ValidationException(emailError,
                new Dictionary<string, string[]> { [nameof(request.Email)] = [emailError] });
        }

        if (!Validators.IsStrongPassword(request.Password, out var passwordError))
        {
            throw new ValidationException(passwordError,
                new Dictionary<string, string[]> { [nameof(request.Password)] = [passwordError] });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        if (await users.GetByUsernameAsync(normalizedUsername, ct) is not null)
        {
            throw new ConflictException("That username is already taken.", "USERNAME_TAKEN");
        }

        if (await users.GetByEmailAsync(normalizedEmail, ct) is not null)
        {
            throw new ConflictException("An account with that email already exists.", "EMAIL_TAKEN");
        }

        var user = new User
        {
            Username = normalizedUsername,
            Email = normalizedEmail,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? request.Username
                : Validators.SanitizeName(request.DisplayName) ?? request.Username,
            PasswordHash = passwordHasher.Hash(request.Password),
            StorageQuotaBytes = _storage.DefaultUserQuotaBytes,
        };

        // The default role is data, not code — resolved through the role repository.
        var defaultRole = await roles.GetByNameAsync(RoleNames.User, ct)
            ?? throw new ConflictException("The default role is not configured. Contact an administrator.",
                "ROLE_MISSING");
        user.Roles.Add(new UserRole { RoleId = defaultRole.Id });

        users.Add(user);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.UserRegistered,
            $"User '{user.Username}' registered.", user.Id, "User", user.Id.ToString(), ipAddress, ct);
        logger.LogInformation("User {Username} registered", user.Username);

        return await IssueTokensAsync(user, ipAddress, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var identifier = request.Login.Trim().ToLowerInvariant();
        var user = await users.GetByUsernameAsync(identifier, ct)
            ?? await users.GetByEmailAsync(identifier, ct);

        // Always run one hash verification to avoid a trivial user-enumeration timing oracle.
        var hash = user?.PasswordHash ?? passwordHasher.Hash("__timing_equalizer__");
        var valid = passwordHasher.Verify(hash, request.Password);
        if (user is null || !valid)
        {
            await activity.LogAsync(ActivityAction.UserLoginFailed,
                $"Failed login attempt for '{identifier}'.", user?.Id, "User", null, ipAddress, ct);
            logger.LogWarning("Failed login for {Identifier}", identifier);
            throw new UnauthorizedException("Invalid credentials.");
        }

        if (user.IsDisabled)
        {
            await activity.LogAsync(ActivityAction.UserLoginFailed,
                $"Disabled account '{user.Username}' attempted to log in.", user.Id, "User",
                user.Id.ToString(), ipAddress, ct);
            throw new ForbiddenException("This account has been disabled.", "ACCOUNT_DISABLED");
        }

        user.LastLoginAt = DateTime.UtcNow;
        users.Update(user);
        await unitOfWork.SaveChangesAsync(ct);

        await activity.LogAsync(ActivityAction.UserLoggedIn,
            $"User '{user.Username}' logged in.", user.Id, "User", user.Id.ToString(), ipAddress, ct);

        return await IssueTokensAsync(user, ipAddress, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken ct = default)
    {
        var tokenHash = tokenGenerator.HashToken(refreshToken);
        var stored = await refreshTokens.GetByTokenHashAsync(tokenHash, ct);

        if (stored is null || !stored.IsActive)
        {
            logger.LogWarning("Rejected refresh token (unknown, expired or revoked)");
            throw new UnauthorizedException("Invalid or expired refresh token.");
        }

        var user = await users.GetWithRolesAsync(stored.UserId, ct)
            ?? throw new UnauthorizedException("Invalid or expired refresh token.");

        if (user.IsDisabled)
        {
            await refreshTokens.RevokeAllForUserAsync(user.Id, "Account disabled", ct);
            await unitOfWork.SaveChangesAsync(ct);
            throw new ForbiddenException("This account has been disabled.", "ACCOUNT_DISABLED");
        }

        // Rotation: revoke the presented token and issue a replacement in the same save.
        stored.RevokedAt = DateTime.UtcNow;
        stored.RevokedReason = "Replaced by refresh";
        refreshTokens.Update(stored);

        var replacement = await CreateRefreshTokenAsync(user.Id, ipAddress, ct);
        stored.ReplacedByTokenHash = tokenGenerator.HashToken(replacement);
        await unitOfWork.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = await IssueAccessTokenAsync(user, ct);
        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = replacement,
            AccessTokenExpiresAtUtc = expiresAt,
            User = await ToUserDtoAsync(user, ct),
        };
    }

    public async Task LogoutAsync(string refreshToken, string? ipAddress, CancellationToken ct = default)
    {
        var tokenHash = tokenGenerator.HashToken(refreshToken);
        var stored = await refreshTokens.GetByTokenHashAsync(tokenHash, ct);

        if (stored is not null && stored.IsActive)
        {
            stored.RevokedAt = DateTime.UtcNow;
            stored.RevokedReason = "Logged out";
            refreshTokens.Update(stored);
            await unitOfWork.SaveChangesAsync(ct);
            await activity.LogAsync(ActivityAction.UserLoggedOut,
                "User logged out.", stored.UserId, "User", stored.UserId.ToString(), ipAddress, ct);
        }

        // Unknown tokens log out silently — no information leak.
    }

    public async Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.GetWithRolesAsync(userId, ct)
            ?? throw new NotFoundException("User not found.");
        return await ToUserDtoAsync(user, ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, string? ipAddress, CancellationToken ct)
    {
        var refresh = await CreateRefreshTokenAsync(user.Id, ipAddress, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = await IssueAccessTokenAsync(user, ct);
        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refresh,
            AccessTokenExpiresAtUtc = expiresAt,
            User = await ToUserDtoAsync(user, ct),
        };
    }

    private Task<string> CreateRefreshTokenAsync(Guid userId, string? ipAddress, CancellationToken ct)
    {
        var raw = tokenGenerator.GenerateToken(64);
        refreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenGenerator.HashToken(raw),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays),
            RemoteIpAddress = ipAddress,
        });
        return Task.FromResult(raw);
    }

    private async Task<(string Token, DateTime ExpiresAt)> IssueAccessTokenAsync(User user, CancellationToken ct)
    {
        var loaded = user.Roles.Count > 0
            ? user
            : await users.GetWithRolesAsync(user.Id, ct) ?? user;
        var roleNames = loaded.Roles.Select(r => r.Role.Name).ToList();
        var permissions = loaded.Roles
            .SelectMany(r => r.Role.Permissions)
            .Select(p => p.Permission.Name)
            .Distinct()
            .ToList();
        return jwtService.IssueAccessToken(loaded, roleNames, permissions);
    }

    private async Task<UserDto> ToUserDtoAsync(User user, CancellationToken ct)
    {
        var permissions = await permissionChecker.GetPermissionsAsync(user.Id, ct);
        return user.ToDto(permissions);
    }
}
