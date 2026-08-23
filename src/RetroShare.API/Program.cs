using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RetroShare.API;
using RetroShare.API.Authorization;
using RetroShare.API.Health;
using RetroShare.API.Middleware;
using RetroShare.Application;
using RetroShare.Application.Common;
using RetroShare.Domain.Constants;
using RetroShare.Infrastructure;
using RetroShare.Infrastructure.Data;
using RetroShare.Infrastructure.Grpc;
using Serilog;

// ---------------------------------------------------------------------------
// Serilog bootstrap — plain static console logger for pre-host failures.
// Host logging is wired per-application via AddSerilog below (no reloadable
// static logger, which cannot be shared by more than one host per process).
// ---------------------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ---------------------------------------------------------------------------
    // Layers
    // ---------------------------------------------------------------------------
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

    // gRPC data plane services (server side; gRPC-Web middleware is added below).
    builder.Services.AddGrpc(options =>
    {
        options.MaxReceiveMessageSize = 4 * 1024 * 1024; // 4 MiB streaming frames
        options.MaxSendMessageSize = 4 * 1024 * 1024;
    });

    // ---------------------------------------------------------------------------
    // Authentication: JWT bearer for both REST and gRPC (Authorization metadata).
    // Validation parameters are supplied by ConfigureJwtBearerOptions so the secret
    // resolves from the merged configuration, not from a snapshot taken here.
    // ---------------------------------------------------------------------------
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer();
    builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();

    builder.Services.AddAuthorization(options =>
    {
        // One policy per known permission — controllers authorize against these names.
        foreach (var (name, _, _) in Permissions.All)
        {
            options.AddPolicy(name, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(name)));
        }
    });
    builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionAuthorizationHandler>();

    // ---------------------------------------------------------------------------
    // API plumbing
    // ---------------------------------------------------------------------------
    builder.Services.AddControllers().AddJsonOptions(o =>
        o.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(SetupSwagger.Configure);
    builder.Services.AddHttpContextAccessor();

    // Consistent error envelope for model-state failures.
    builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(kv => kv.Value?.Errors.Count > 0)
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                new ApiErrorResponse("Validation failed.", "VALIDATION_FAILED", errors));
        };
    });

    // Rate limiting: a generous global bucket and a strict bucket for auth endpoints.
    // Can be disabled (e.g. in tests) via RateLimit:Enabled=false.
    if (builder.Configuration.GetValue("RateLimit:Enabled", true))
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(new ApiErrorResponse(
                    "Too many requests. Slow down.", "RATE_LIMITED"), ct);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.AddPolicy(AuthRateLimit.Policy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });
    }

    // CORS: same-origin by default (frontend is hosted by this app); configurable for split deploys.
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (allowedOrigins.Length > 0)
    {
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()));
    }

    // Health checks: database + storage + gRPC service availability.
    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database")
        .AddCheck<StorageHealthCheck>("storage");

    var app = builder.Build();

    // Production configuration guard: refuse to run with development secrets.
    if (app.Environment.IsProduction())
    {
        var jwt = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtOptions>>().Value;
        if (string.IsNullOrEmpty(jwt.Secret) || Encoding.UTF8.GetByteCount(jwt.Secret) < 32)
        {
            throw new InvalidOperationException(
                "Set the Jwt__Secret environment variable to at least 32 characters in production.");
        }

        var seed = app.Configuration.GetSection(SeedOptions.SectionName).Get<SeedOptions>() ?? new SeedOptions();
        if (seed.AdminPassword == "ChangeMe!123")
        {
            throw new InvalidOperationException(
                "Set Seed__AdminPassword to a real password in production.");
        }
    }

    // ---------------------------------------------------------------------------
// Middleware pipeline
// ---------------------------------------------------------------------------
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseSerilogRequestLogging();

    if (allowedOrigins.Length > 0)
    {
        app.UseCors();
    }

    if (app.Configuration.GetValue("RateLimit:Enabled", true))
    {
        app.UseRateLimiter();
    }    app.UseAuthentication();
    app.UseAuthorization();

    // gRPC data plane (with gRPC-Web so browser JavaScript can stream too).
    app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
    app.MapGrpcService<FileTransferGrpcService>();

    app.MapControllers();

    app.MapHealthChecks("/api/health", new HealthCheckOptions
    {
        ResponseWriter = HealthResponseWriter.WriteJson,
    });

    // ---------------------------------------------------------------------------
    // Frontend: static retro UI served from src/RetroShare.Web/wwwroot when running from
    // source, or from the published wwwroot otherwise.
    // ---------------------------------------------------------------------------
    var devWebRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
        app.Environment.ContentRootPath, "..", "RetroShare.Web", "wwwroot"));
    if (System.IO.Directory.Exists(devWebRoot))
    {
        app.Logger.LogInformation("Serving frontend from {Path}", devWebRoot);
        app.Environment.WebRootFileProvider =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(devWebRoot);
    }

    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            // Frontend assets are plain files with no build step; never let the
            // browser serve a stale module after an edit. Negligible cost at
            // this scale, and only applies to static responses.
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        },
    });

    // Pretty URL for public share links: /s/{token} -> share.html (which reads the token).
    app.Map("/s/{token}", (string token) => Results.Redirect($"/share.html?token={token}"));

    // Fallback: unknown non-API routes land on the app shell.
    app.MapFallbackToFile("index.html");

    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "RetroShare API v1");
            options.DocumentTitle = "RetroShare API";
        });
    }

    // Migrate + seed the database before accepting traffic.
    await DbInitializer.InitializeAsync(app.Services);

    Log.Information("RetroShare starting");
    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException) // design-time host abort (ef migrations)
{
    Log.Fatal(ex, "RetroShare terminated unexpectedly");
    Console.Error.WriteLine($"FACTORY_START_FAILURE: {ex}");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Marker for WebApplicationFactory in integration tests.</summary>
public partial class Program { }
