using Microsoft.Data.Sqlite;

namespace NetWatch.Server.Data;

internal sealed class MigrationRunner(SqliteConnectionFactory _connectionFactory, ILogger<MigrationRunner> _logger)
{
    private static readonly Migration[] _migrations =
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
            """),
        new(
            2,
            "device-discovery",
            """
            CREATE TABLE devices (
                id TEXT PRIMARY KEY,
                mac_address TEXT NULL UNIQUE,
                friendly_name TEXT NULL,
                discovered_name TEXT NULL,
                type TEXT NOT NULL,
                notes TEXT NULL,
                is_manual INTEGER NOT NULL,
                is_online INTEGER NOT NULL,
                current_ip_address TEXT NULL,
                dhcp_status TEXT NULL,
                dhcp_dynamic INTEGER NULL,
                dhcp_server TEXT NULL,
                dhcp_comment TEXT NULL,
                last_seen TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                archived_at_utc TEXT NULL
            );

            CREATE TABLE ip_assignments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                device_id TEXT NOT NULL REFERENCES devices(id),
                ip_address TEXT NOT NULL,
                source TEXT NOT NULL,
                valid_from_utc TEXT NOT NULL,
                valid_to_utc TEXT NULL
            );

            CREATE INDEX ix_ip_assignments_ip_time
                ON ip_assignments(ip_address, valid_from_utc, valid_to_utc);
            CREATE INDEX ix_ip_assignments_device_time
                ON ip_assignments(device_id, valid_from_utc DESC);
            CREATE UNIQUE INDEX ux_ip_assignments_open_device_ip
                ON ip_assignments(device_id, ip_address) WHERE valid_to_utc IS NULL;

            CREATE TABLE router_state (
                singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
                router_os_version TEXT NOT NULL,
                is_supported INTEGER NOT NULL,
                detected_at_utc TEXT NOT NULL,
                last_dhcp_sync_at_utc TEXT NULL
            );
            """)
    ];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

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

        foreach (var migration in _migrations)
        {
            if (await IsAppliedAsync(connection, migration.Version, cancellationToken))
            {
                continue;
            }

            await ApplyAsync(connection, migration, cancellationToken);
            _logger.LogInformation(
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
