using Dapper;
using Npgsql;

namespace HookRelay.Data;

public sealed partial class Migrator
{
    private readonly Db _db;
    private readonly ILogger<Migrator> _logger;
    private readonly string _migrationsDirectory;

    public Migrator(Db db, ILogger<Migrator> logger, string migrationsDirectory)
    {
        _db = db;
        _logger = logger;
        _migrationsDirectory = migrationsDirectory;
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await using var connection = await _db.OpenInitialAsync(ct);

        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                filename text PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now()
            )
            """);

        var applied = new HashSet<string>(
            await connection.QueryAsync<string>("SELECT filename FROM schema_migrations"),
            StringComparer.Ordinal);

        foreach (var file in EnumerateMigrations())
        {
            if (!applied.Contains(file.Name))
            {
                await ApplyAsync(connection, file, ct);
            }
        }
    }

    private IEnumerable<FileInfo> EnumerateMigrations() =>
        new DirectoryInfo(_migrationsDirectory)
            .EnumerateFiles("*.sql")
            .OrderBy(f => f.Name, StringComparer.Ordinal);

    private async Task ApplyAsync(NpgsqlConnection connection, FileInfo file, CancellationToken ct)
    {
        var sql = await File.ReadAllTextAsync(file.FullName, ct);

        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(sql, transaction: transaction);
        await connection.ExecuteAsync(
            "INSERT INTO schema_migrations (filename, applied_at) VALUES (@name, now())",
            new { name = file.Name },
            transaction: transaction);
        await transaction.CommitAsync(ct);

        LogMigrationApplied(_logger, file.Name);
    }

    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Information,
        Message = "Applied migration {Migration}")]
    private static partial void LogMigrationApplied(ILogger logger, string migration);
}
