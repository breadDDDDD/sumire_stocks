using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Sumire.Api.Data;

public static partial class Db
{
    public const string EnvVar = "SUPABASE_CONNECTION_STRING";

    public static bool IsConfigured => ConnectionString() != "";

    public static string NotConfiguredReason => ConfigError ?? $"{EnvVar} is not set";

    /// <summary>Set when the env var holds a value that cannot be parsed (the value itself is never kept or shown).</summary>
    public static string? ConfigError { get; private set; }

    /// <summary>
    /// Reads the env var (key=value or postgresql:// URI form) and adds the settings the Supabase
    /// transaction pooler (port 6543) needs. Returns "" when missing or invalid.
    /// </summary>
    public static string ConnectionString(string? raw = null)
    {
        raw ??= Environment.GetEnvironmentVariable(EnvVar);
        raw = Unquote(raw);
        if (raw == "") { ConfigError = null; return ""; }
        try
        {
            var csb = Parse(raw);
            if (csb.Port == 6543)
            {
                csb.MaxAutoPrepare = 0;
                csb.NoResetOnClose = true;
            }
            ConfigError = null;
            return csb.ConnectionString;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            ConfigError = $"{EnvVar} is not a valid connection string or postgresql:// URI";
            return "";
        }
    }

    // Secrets pasted into CI or .env sometimes keep their surrounding quotes or a trailing newline.
    static string Unquote(string? raw)
    {
        var s = raw?.Trim() ?? "";
        while (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[^1] == s[0]) s = s[1..^1].Trim();
        return s;
    }

    static NpgsqlConnectionStringBuilder Parse(string raw)
    {
        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) && !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return new NpgsqlConnectionStringBuilder(raw);

        // postgresql://user:password@host:port/database?sslmode=require
        // The password may contain unescaped '@', ':', '/' or '?', so split at the last '@' first.
        var rest = raw[(raw.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var at = rest.LastIndexOf('@');
        var userInfo = at >= 0 ? rest[..at] : "";
        var hostPart = at >= 0 ? rest[(at + 1)..] : rest;
        var query = "";
        var q = hostPart.IndexOf('?');
        if (q >= 0) { query = hostPart[(q + 1)..]; hostPart = hostPart[..q]; }
        var slash = hostPart.IndexOf('/');
        var database = slash >= 0 ? hostPart[(slash + 1)..] : "";
        var hostPort = slash >= 0 ? hostPart[..slash] : hostPart;
        // [ipv6]:port keeps its colons inside the brackets
        var colon = hostPort.LastIndexOf(':');
        if (colon >= 0 && hostPort.StartsWith('[') && colon < hostPort.IndexOf(']')) colon = -1;
        var host = (colon >= 0 ? hostPort[..colon] : hostPort).TrimStart('[').TrimEnd(']');
        if (host == "") throw new FormatException("missing host");

        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = colon >= 0 ? int.Parse(hostPort[(colon + 1)..]) : 5432,
            Database = database == "" ? "postgres" : Uri.UnescapeDataString(database),
        };
        if (userInfo != "")
        {
            var c = userInfo.IndexOf(':');
            csb.Username = Uri.UnescapeDataString(c >= 0 ? userInfo[..c] : userInfo);
            if (c >= 0) csb.Password = Uri.UnescapeDataString(userInfo[(c + 1)..]);
        }
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SslMode>(kv[1].Replace("-", ""), ignoreCase: true, out var mode))
                csb.SslMode = mode;
        }
        return csb;
    }

    public static void Configure(DbContextOptionsBuilder o, string connectionString) =>
        o.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__ef_migrations_history", SumireDbContext.Schema))
         .UseSnakeCaseNamingConvention();

    public static IServiceCollection AddSumireDb(this IServiceCollection services)
    {
        var cs = ConnectionString();
        return services.AddDbContext<SumireDbContext>(o => Configure(o, cs));
    }

    /// <summary>Applies pending migrations; logs a friendly error and returns false when the DB is unreachable.</summary>
    public static async Task<bool> TryMigrateAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("{Reason}; running without a database", NotConfiguredReason);
            return false;
        }
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SumireDbContext>();
            await db.Database.MigrateAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError("Database unavailable, migrations not applied: {Error}", ShortError(ex));
            return false;
        }
    }

    /// <summary>One-line error safe to show or log: no quoted values (user names, hosts), no connection string.</summary>
    public static string ShortError(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null && inner is not NpgsqlException && inner is not PostgresException) inner = inner.InnerException;
        var message = inner.Message.Split('\n')[0].Trim();
        message = QuotedRegex().Replace(message, "\"…\"");
        message = HostPortRegex().Replace(message, "…");
        message = HostNameRegex().Replace(message, "…");
        if (message.Length > 160) message = message[..160] + "…";
        var hint = inner is System.Net.Sockets.SocketException || inner.InnerException is System.Net.Sockets.SocketException
            ? " (use the Supabase pooler connection string; the direct db host is IPv6-only)"
            : "";
        return $"{inner.GetType().Name}: {message}{hint}";
    }

    [GeneratedRegex("\"[^\"]*\"|'[^']*'")]
    private static partial Regex QuotedRegex();

    // e.g. "Failed to connect to [2406:da18::1]:5432" or "10.0.0.1:6543"
    [GeneratedRegex(@"(\[[0-9A-Fa-f:.]+\]|[\w.-]+):\d{2,5}\b")]
    private static partial Regex HostPortRegex();

    // dotted names and IPv4 addresses, e.g. db.abc.supabase.co
    [GeneratedRegex(@"\b[\w-]+(\.[\w-]+){2,}\b")]
    private static partial Regex HostNameRegex();
}

/// <summary>Used by dotnet-ef; works without database access.</summary>
public class SumireDbContextFactory : IDesignTimeDbContextFactory<SumireDbContext>
{
    public SumireDbContext CreateDbContext(string[] args)
    {
        var cs = Db.ConnectionString();
        if (cs == "") cs = "Host=localhost;Database=sumire_design;Username=design;Password=design";
        var options = new DbContextOptionsBuilder<SumireDbContext>();
        Db.Configure(options, cs);
        return new SumireDbContext(options.Options);
    }
}
