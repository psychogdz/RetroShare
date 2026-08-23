using System.Text.Json.Serialization;
using RetroShare.Application.Common;

namespace RetroShare.API.Middleware;

/// <summary>Consistent machine-readable error envelope: {"success":false,"message":...,"code":...}.
/// Internal exceptions are logged server-side; clients only see generic messages outside
/// development.</summary>
public sealed record ApiErrorResponse(
    string Message,
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public bool Success => false;
}

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            var (status, body) = Map(ex);
            await WriteAsync(context, status, body);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away mid-transfer; nothing to report.
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogWarning("Access denied for {Path}", context.Request.Path);
            await WriteAsync(context, StatusCodes.Status403Forbidden,
                new ApiErrorResponse("You do not have permission to perform this action.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            // Outside development, never leak exception details to clients.
            var message = environment.IsDevelopment()
                ? $"{ex.GetType().Name}: {ex.Message}"
                : "An unexpected error occurred.";
            await WriteAsync(context, StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(message, "INTERNAL_ERROR"));
        }
    }

    private static (int Status, ApiErrorResponse Body) Map(AppException ex) => ex switch
    {
        ValidationException v => (StatusCodes.Status400BadRequest,
            new ApiErrorResponse(v.Message, v.ErrorCode, v.Errors)),
        UnauthorizedException => (StatusCodes.Status401Unauthorized, new ApiErrorResponse(ex.Message, ex.ErrorCode)),
        ForbiddenException or ShareAccessException => (StatusCodes.Status403Forbidden,
            new ApiErrorResponse(ex.Message, ex.ErrorCode)),
        NotFoundException => (StatusCodes.Status404NotFound, new ApiErrorResponse(ex.Message, ex.ErrorCode)),
        ConflictException => (StatusCodes.Status409Conflict, new ApiErrorResponse(ex.Message, ex.ErrorCode)),
        StorageLimitException => (StatusCodes.Status413PayloadTooLarge, new ApiErrorResponse(ex.Message, ex.ErrorCode)),
        _ => (StatusCodes.Status500InternalServerError, new ApiErrorResponse("An unexpected error occurred.", "INTERNAL_ERROR")),
    };

    private static async Task WriteAsync(HttpContext context, int status, ApiErrorResponse body)
    {
        if (context.Response.HasStarted)
        {
            return; // streaming response — too late to change the status code
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(body);
    }
}
