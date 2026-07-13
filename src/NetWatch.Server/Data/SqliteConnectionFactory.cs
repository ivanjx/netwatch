using Microsoft.Data.Sqlite;

namespace NetWatch.Server.Data;

internal sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory()
        : this(FixedDatabasePath)
    {
    }

    internal SqliteConnectionFactory(string databasePath)
    {
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();
    }

    internal string DatabasePath { get; }

    private const string FixedDatabasePath = "/data/netwatch.db";

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 5000;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
