using System.Security.Claims;

namespace RetroShare.API.Extensions;

/// <summary>Helper for extracting the caller identity from JWT claims.</summary>
public static class ClaimsExtensions
{
    /// <summary>The authenticated user's id, or null when anonymous.</summary>
    public static Guid? TryGetUserId(this ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var claim = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        TryGetUserId(principal)
        ?? throw new ApplicationException("Authenticated caller without a user id claim.");

    public static string? TryGetRemoteIp(this HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString();
}
