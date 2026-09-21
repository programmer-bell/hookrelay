using System.Net;
using Npgsql;

namespace HookRelay.Data;

public sealed partial class Db : IAsyncDisposable
{
    private readonly ILogger<Db> _logger;

    private Db(NpgsqlDataSource dataSource, ILogger<Db> logger)
    {
        DataSource = dataSource;
        _logger = logger;
    }

    public NpgsqlDataSource DataSource { get; }

    public static Db Open(IConfiguration configuration, ILogger<Db> logger) =>
        new(NpgsqlDataSource.Create(NormalizeConnectionString(configuration["DATABASE_URL"])), logger);

    public static string NormalizeConnectionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("DATABASE_URL is not configured.");
        }

        var builder = raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                      raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            ? FromUri(raw)
            : new NpgsqlConnectionStringBuilder(raw);

        builder.Timeout = 30;
        builder.CommandTimeout = 30;

        if (!IsLocalConnection(builder.Host) && builder.SslMode == SslMode.Prefer)
        {
            builder.SslMode = SslMode.Require;
        }

        return builder.ConnectionString;
    }

    public async Task<NpgsqlConnection> OpenInitialAsync(CancellationToken ct = default)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await DataSource.OpenConnectionAsync(ct);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                LogConnectionAttemptFailed(_logger, attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    public async ValueTask DisposeAsync() => await DataSource.DisposeAsync();

    private static NpgsqlConnectionStringBuilder FromUri(string value)
    {
        var uri = new Uri(value);
        var credentials = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port == -1 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = WebUtility.UrlDecode(credentials[0]),
            Password = credentials.Length == 2 ? WebUtility.UrlDecode(credentials[1]) : null,
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = (separator < 0 ? pair : pair[..separator]).ToLowerInvariant();
            var queryValue = separator < 0 ? string.Empty : WebUtility.UrlDecode(pair[(separator + 1)..]);

            if (key is "sslmode" && TryGetSslMode(queryValue, out var sslMode))
            {
                builder.SslMode = sslMode;
            }
            else if (key == "application_name")
            {
                builder.ApplicationName = queryValue;
            }
        }

        return builder;
    }

    private static bool TryGetSslMode(string value, out SslMode sslMode)
    {
        switch (value.ToLowerInvariant())
        {
            case "disable":
                sslMode = SslMode.Disable;
                return true;
            case "prefer":
                sslMode = SslMode.Prefer;
                return true;
            case "require":
                sslMode = SslMode.Require;
                return true;
            case "verify-ca":
            case "verify-full":
                sslMode = SslMode.VerifyFull;
                return true;
            default:
                sslMode = default;
                return false;
        }
    }

    private static bool IsLocalConnection(string? host)
    {
        if (string.IsNullOrEmpty(host) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            return IPAddress.IsLoopback(ip) || IsPrivateAddress(ip);
        }

        return !host.Contains('.');
    }

    private static bool IsPrivateAddress(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254);
        }

        return (bytes[0] & 0xfe) == 0xfc ||
               (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80);
    }

    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Warning,
        Message = "Database connection attempt {Attempt}/{Max} failed: {Error}")]
    private static partial void LogConnectionAttemptFailed(ILogger logger, int attempt, int max, string error);
}
