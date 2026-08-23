namespace RetroShare.Domain.Constants;

/// <summary>Domain-wide limits and validation rules. Values are defaults; runtime limits
/// (max upload size, default quota) are configurable and enforced in the Application layer.</summary>
public static class FileRules
{
    public const int MinNameLength = 1;

    public const int MaxNameLength = 255;

    /// <summary>Hard cap for a single streamed upload, configurable via Storage:MaxFileSizeBytes.</summary>
    public const long DefaultMaxFileSizeBytes = 2L * 1024 * 1024 * 1024; // 2 GiB

    /// <summary>Default per-user quota, configurable via Storage:DefaultUserQuotaBytes.</summary>
    public const long DefaultUserQuotaBytes = 10L * 1024 * 1024 * 1024; // 10 GiB

    /// <summary>Chunk size used by the gRPC data plane for streaming (server side).</summary>
    public const int StreamingChunkSize = 64 * 1024;

    /// <summary>Executable file extensions that are never accepted, regardless of
    /// configuration. Files are streamed as downloads (never served inline from the app's own
    /// origin), so script/text types remain shareable.</summary>
    public static readonly HashSet<string> BlockedExtensions =
    [
        ".exe", ".bat", ".cmd", ".com", ".scr", ".ps1", ".msi", ".vbs", ".vbe", ".hta", ".cpl",
    ];

    /// <summary>MIME types that are never accepted (defense-in-depth next to extension checks).</summary>
    public static readonly HashSet<string> BlockedMimeTypes =
    [
        "application/x-msdownload",
        "application/x-executable",
        "application/x-bat",
        "application/x-msdos-program",
    ];
}
