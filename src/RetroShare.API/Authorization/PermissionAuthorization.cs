using Microsoft.AspNetCore.Authorization;
using RetroShare.Application.Interfaces;

namespace RetroShare.API.Authorization;

/// <summary>Requirement carrying the permission name checked by <see cref="PermissionAuthorizationHandler"/>.</summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Grants the requirement when the authenticated user currently holds the permission
/// according to the database (via <see cref="IPermissionChecker"/>), making permission changes
/// effective immediately without re-issuing tokens.</summary>
public sealed class PermissionAuthorizationHandler(IPermissionChecker permissionChecker)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (await permissionChecker.HasPermissionAsync(context.User, requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Rate-limit policy name for credential endpoints.</summary>
public static class AuthRateLimit
{
    public const string Policy = "auth";
}
