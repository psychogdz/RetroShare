using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RetroShare.API.Authorization;
using RetroShare.API.Extensions;
using RetroShare.Application.Common;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;

namespace RetroShare.API.Controllers;

/// <summary>Registration, login, token refresh, logout and current-user introspection.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>Creates an account and returns an initial token pair.</summary>
    [HttpPost("register")]
    [EnableRateLimiting(AuthRateLimit.Policy)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request, HttpContext.TryGetRemoteIp(), ct);
        return Ok(result);
    }

    /// <summary>Exchanges credentials for an access/refresh token pair.</summary>
    [HttpPost("login")]
    [EnableRateLimiting(AuthRateLimit.Policy)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, HttpContext.TryGetRemoteIp(), ct);
        return Ok(result);
    }

    /// <summary>Rotates a refresh token (the presented token is revoked and replaced).</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting(AuthRateLimit.Policy)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await auth.RefreshAsync(request.RefreshToken, HttpContext.TryGetRemoteIp(), ct);
        return Ok(result);
    }

    /// <summary>Revokes the presented refresh token.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(request.RefreshToken, HttpContext.TryGetRemoteIp(), ct);
        return NoContent();
    }

    /// <summary>Returns the authenticated user's profile, roles and permissions.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        Ok(await auth.GetMeAsync(User.GetUserId(), ct));
}
