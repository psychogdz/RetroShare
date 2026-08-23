using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RetroShare.Application.Common;
using System.Text;

namespace RetroShare.API.Authorization;

/// <summary>Configures JWT bearer validation from the final JwtOptions binding. Configured
/// via IConfigureOptions so the secret is read after all configuration sources (including
/// environment overrides and test-host in-memory config) have been merged.</summary>
public sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwtOptions)
    : IConfigureNamedOptions<JwtBearerOptions>, IConfigureOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var jwt = jwtOptions.Value;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
