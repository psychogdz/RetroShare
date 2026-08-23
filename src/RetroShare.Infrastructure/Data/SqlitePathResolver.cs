using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace RetroShare.Infrastructure.Data;

/// <summary>Resolves the physical SQLite database file path from the connection string,
/// applying the same content-root resolution as the DbContext configuration. Returns null
/// for in-memory databases.</summary>
public static class SqlitePathResolver
{
    public static string? ResolveDatabaseFile(IConfiguration configuration, string contentRootPath)
    {
        var raw = configuration.GetConnectionString("Database") ?? "Data Source=retroshare.db";
        var builder = new SqliteConnectionStringBuilder(raw);
        var source = builder.DataSource;
        if (string.IsNullOrEmpty(source) || source.Contains("mode=memory", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.IsPathRooted(source) ? source : Path.GetFullPath(Path.Combine(contentRootPath, source));
    }
}
