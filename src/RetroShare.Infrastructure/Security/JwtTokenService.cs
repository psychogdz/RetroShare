using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RetroShare.Application.Common;
using RetroShare.Application.Interfaces;
using RetroShare.Domain.Entities;

namespace RetroShare.Infrastructure.Security;

/// <summary>Signs short-lived HS256 JWT access tokens. Roles are embedded as role claims;
/// permissions are mirrored as "perm" claims for display, but authorization decisions always
/// re-resolve live from the database (see PermissionChecker).</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAtUtc) IssueAccessToken(User user,
        IEnumerable<string> roleNames, IEnumerable<string> permissionNames)
    {
        if (string.IsNullOrEmpty(_options.Secret) || Encoding.UTF8.GetByteCount(_options.Secret) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Secret must be configured to at least 32 bytes before issuing tokens.");
        }

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("username", user.Username),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
        };
        claims.AddRange(roleNames.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissionNames.Select(permission => new Claim("perm", permission)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
