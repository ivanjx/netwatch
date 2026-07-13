using Microsoft.Data.Sqlite;

using NetWatch.Server.Common;
using NetWatch.Server.Data;

namespace NetWatch.Server.Health;

internal sealed class SystemStatusRepository(
    SqliteConnectionFactory connectionFactory,
    ILogger<SystemStatusRepository> logger)
{
    public async Task<RepositoryResult> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
            var version = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return new RepositoryResult<DatabaseStatus>(new DatabaseStatus(version));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read database status");
            var detail = exception is SqliteException sqliteException ?
                $"SQLite error {sqliteException.SqliteErrorCode}." :
                "The database dependency failed unexpectedly.";
            return new DatabaseUnavailableRepositoryErrorResult(detail);
        }
    }
}
