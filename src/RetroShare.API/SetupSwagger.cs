using System.Reflection;
using Microsoft.OpenApi.Models;

namespace RetroShare.API;

/// <summary>Swagger/OpenAPI configuration: JWT security scheme, XML-less summaries from
/// endpoint names, and error response documentation on the common endpoints.</summary>
public static class SetupSwagger
{
    public static void Configure(Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options)
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "RetroShare API",
            Version = "v1",
            Description = "Control-plane REST API for the RetroShare file-sharing platform. "
                + "File bytes move over the gRPC data plane (FileTransfer service with gRPC-Web), "
                + "not through these endpoints.",
            Contact = new OpenApiContact { Name = "RetroShare" },
            License = new OpenApiLicense { Name = "MIT" },
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the JWT access token returned by /api/auth/login.",
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                },
                Array.Empty<string>()
            },
        });

        options.OrderActionsBy(api => api.RelativePath);
    }
}
