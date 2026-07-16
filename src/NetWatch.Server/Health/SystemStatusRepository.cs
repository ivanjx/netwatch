using NetWatch.Server.Common;
using NetWatch.Server.Data;

namespace NetWatch.Server.Health;

internal sealed class SystemStatusRepository(
    SqliteConnectionFactory _connectionFactory,
    ILogger<SystemStatusRepository> _logger)
{
    public async Task<RepositoryResult> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
            var version = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return new RepositoryResult<DatabaseStatus>(new DatabaseStatus(version));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledRepositoryErrorResult();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to read database status");
            return new DatabaseUnavailableRepositoryErrorResult();
        }
    }
}
