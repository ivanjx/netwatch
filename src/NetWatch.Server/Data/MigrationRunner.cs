using Microsoft.Data.Sqlite;

namespace NetWatch.Server.Data;

internal sealed class MigrationRunner(SqliteConnectionFactory connectionFactory, ILogger<MigrationRunner> logger)
{
    private static readonly Migration[] Migrations =
    [
        new(
            1,
            "foundation",
            """
            CREATE TABLE app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """)
    ];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        await using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = "PRAGMA journal_mode = WAL;";
            await pragmas.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var initialize = connection.CreateCommand())
        {
            initialize.CommandText =
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at_utc TEXT NOT NULL
                );
                """;
            await initialize.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var migration in Migrations)
        {
            if (await IsAppliedAsync(connection, migration.Version, cancellationToken))
            {
                continue;
            }

            await ApplyAsync(connection, migration, cancellationToken);
            logger.LogInformation(
                "Applied database migration {MigrationVersion} {MigrationName}",
                migration.Version,
                migration.Name);
        }
    }

    private static async Task<bool> IsAppliedAsync(
        SqliteConnection connection,
        long version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE version = $version);";
        command.Parameters.AddWithValue("$version", version);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task ApplyAsync(
        SqliteConnection connection,
        Migration migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            $"""
            {migration.Sql}
            INSERT INTO schema_migrations (version, name, applied_at_utc)
            VALUES ($version, $name, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$version", migration.Version);
        command.Parameters.AddWithValue("$name", migration.Name);
        command.Parameters.AddWithValue("$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record Migration(long Version, string Name, string Sql);
}
