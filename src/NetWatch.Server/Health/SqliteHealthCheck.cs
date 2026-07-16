using Microsoft.Extensions.Diagnostics.HealthChecks;

using NetWatch.Server.Data;

namespace NetWatch.Server.Health;

internal sealed class SqliteHealthCheck(SqliteConnectionFactory _connectionFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM schema_migrations LIMIT 1;";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("SQLite is available.");
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("SQLite health check was canceled.", exception);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("SQLite is unavailable.", exception);
        }
    }
}
