using Microsoft.Data.Sqlite;

using NetWatch.Server.Configuration;

namespace NetWatch.Server.Data;

internal sealed class SqliteSpikeStore(SpikeOptions options, ILogger<SqliteSpikeStore> logger)
{
    public async Task<SqliteSpikeResult> WriteAndReadAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(options.DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var initialize = connection.CreateCommand())
        {
            initialize.CommandText =
                """
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 5000;
                CREATE TABLE IF NOT EXISTS phase0_probe (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    write_count INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );
                """;
            await initialize.ExecuteNonQueryAsync(cancellationToken);
        }

        var timestamp = DateTimeOffset.UtcNow;
        await using (var write = connection.CreateCommand())
        {
            write.CommandText =
                """
                INSERT INTO phase0_probe (id, write_count, updated_at_utc)
                VALUES (1, 1, $updatedAtUtc)
                ON CONFLICT(id) DO UPDATE SET
                    write_count = write_count + 1,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            write.Parameters.AddWithValue("$updatedAtUtc", timestamp.ToString("O"));
            await write.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT write_count, updated_at_utc FROM phase0_probe WHERE id = 1;";
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        var result = new SqliteSpikeResult(
            reader.GetInt64(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            options.DatabasePath);
        logger.LogInformation("SQLite Phase 0 probe completed with persisted write count {WriteCount}", result.WriteCount);
        return result;
    }
}

internal sealed record SqliteSpikeResult(long WriteCount, DateTimeOffset UpdatedAtUtc, string DatabasePath);
