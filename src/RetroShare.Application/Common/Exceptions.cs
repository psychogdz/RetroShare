namespace RetroShare.Application.Common;

/// <summary>Base type for all expected (client-visible) application errors. The API exception
/// middleware maps the exception type to an HTTP status and serializes ErrorCode/Message
/// without leaking internals.</summary>
public abstract class AppException : Exception
{
    public string ErrorCode { get; }

    /// <summary>Optional field-level validation errors keyed by property name.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    protected AppException(string code, string message,
        IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        ErrorCode = code;
        Errors = errors;
    }
}

public sealed class ValidationException : AppException
{
    public ValidationException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base("VALIDATION_FAILED", message, errors) { }
}

public sealed class UnauthorizedException : AppException
{
    public UnauthorizedException(string message = "Authentication required.")
        : base("UNAUTHORIZED", message) { }
}

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message = "You do not have permission to perform this action.",
        string code = "FORBIDDEN")
        : base(code, message) { }
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string message = "Resource not found.")
        : base("NOT_FOUND", message) { }
}

public sealed class ConflictException : AppException
{
    public ConflictException(string message, string code = "CONFLICT")
        : base(code, message) { }
}

/// <summary>Upload rejected because it would exceed a limit (single-file cap or user quota).</summary>
public sealed class StorageLimitException : AppException
{
    public StorageLimitException(string message, string code)
        : base(code, message) { }

    public static StorageLimitException FileTooLarge(long maxSize) =>
        new($"File exceeds the maximum allowed size of {maxSize:N0} bytes.", "FILE_TOO_LARGE");

    public static StorageLimitException QuotaExceeded(long quotaBytes) =>
        new($"Upload would exceed the storage quota of {quotaBytes:N0} bytes.", "QUOTA_EXCEEDED");
}

/// <summary>Upload rejected because the server does not have enough free disk space for the
/// announced file size plus the configured safety reserve. Distinct from quota errors: this is
/// about the machine, not the user's allowance.</summary>
public sealed class InsufficientStorageException : AppException
{
    public InsufficientStorageException(long freeBytes, long requiredBytes)
        : base("INSUFFICIENT_STORAGE",
            $"Insufficient storage space available. Free: {freeBytes:N0} bytes, required: {requiredBytes:N0} bytes.")
    {
        FreeBytes = freeBytes;
        RequiredBytes = requiredBytes;
    }

    public long FreeBytes { get; }

    public long RequiredBytes { get; }
}

/// <summary>A share link exists but cannot currently be used (revoked, expired, exhausted,
/// password-protected without a valid password, or its file was deleted).</summary>
public sealed class ShareAccessException : AppException
{
    public ShareAccessException(string message, string code)
        : base(code, message) { }

    public static ShareAccessException NotFound() =>
        new("Share link not found.", "SHARE_NOT_FOUND");

    public static ShareAccessException Unavailable(string reason) =>
        new($"This share link is no longer available: {reason}.", "SHARE_UNAVAILABLE");

    public static ShareAccessException PasswordRequired() =>
        new("This share link is protected by a password.", "SHARE_PASSWORD_REQUIRED");

    public static ShareAccessException WrongPassword() =>
        new("Incorrect share password.", "SHARE_INVALID_PASSWORD");
}
