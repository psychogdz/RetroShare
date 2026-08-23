using System.Text.RegularExpressions;
using RetroShare.Application.Common;
using RetroShare.Domain.Constants;

namespace RetroShare.Application.Validation;

/// <summary>Pure, unit-testable validation rules used by the Application services.</summary>
public static partial class Validators
{
    [GeneratedRegex(@"^[a-zA-Z0-9_.-]+$")]
    private static partial Regex UsernameRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    private static readonly Regex ControlChars = new(@"[\p{Cc}\p{Cf}]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    /// <summary>Password policy: length plus at least three of the four character classes.</summary>
    public static bool IsStrongPassword(string password, out string error)
    {
        if (password.Length is < MinPasswordLength or > MaxPasswordLength)
        {
            error = $"Password must be between {MinPasswordLength} and {MaxPasswordLength} characters.";
            return false;
        }

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;

        if (classes < 3)
        {
            error = "Password must combine at least three of: lowercase, uppercase, digits, symbols.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsValidUsername(string username, out string error)
    {
        if (username.Length is < 3 or > 32)
        {
            error = "Username must be between 3 and 32 characters.";
            return false;
        }

        if (!UsernameRegex().IsMatch(username))
        {
            error = "Username may only contain letters, digits, '.', '_' and '-'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsValidEmail(string email, out string error)
    {
        if (email.Length > 256 || !EmailRegex().IsMatch(email))
        {
            error = "Enter a valid email address.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Sanitizes a user-supplied display name for a file or folder: strips control
    /// characters, path separators, reserved device names and leading dots (defense-in-depth
    /// against traversal-looking names); collapses whitespace. Returns null when nothing
    /// usable remains. Display names never reach filesystem paths — blobs use generated ids.</summary>
    public static string? SanitizeName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        var name = rawName.Trim();
        name = name.Replace('/', ' ').Replace('\\', ' ');
        name = ControlChars.Replace(name, string.Empty);
        name = name.TrimStart('.', ' ');
        name = WhitespaceRun.Replace(name, " ").Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name.Truncate(FileRules.MaxNameLength);
    }

    private static readonly HashSet<string> ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9",
    ];

    /// <summary>Rejects Windows reserved device names that could confuse filesystem handling.</summary>
    public static bool IsReservedName(string name)
    {
        var stem = name.Split('.')[0].ToUpperInvariant();
        return ReservedNames.Contains(stem);
    }

    /// <summary>Validates extension and MIME type against the block lists.</summary>
    public static void ValidateFileType(string extension, string mimeType)
    {
        if (FileRules.BlockedExtensions.Contains(extension))
        {
            throw new ValidationException($"Files of type '{extension}' are not allowed.");
        }

        if (!string.IsNullOrWhiteSpace(mimeType) && FileRules.BlockedMimeTypes.Contains(mimeType))
        {
            throw new ValidationException($"MIME type '{mimeType}' is not allowed.");
        }
    }

    /// <summary>Validates share creation options.</summary>
    public static void ValidateShareOptions(DateTime? expiresAt, int? maxDownloads)
    {
        if (expiresAt.HasValue)
        {
            var utc = expiresAt.Value.ToUniversalTime();
            if (utc <= DateTime.UtcNow)
            {
                throw new ValidationException("Share expiration must be in the future.");
            }

            if (utc > DateTime.UtcNow.AddDays(365))
            {
                throw new ValidationException("Share expiration may be at most one year ahead.");
            }
        }

        if (maxDownloads is < 1 or > 1_000_000)
        {
            throw new ValidationException("Download limit must be between 1 and 1,000,000.");
        }
    }
}

public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
