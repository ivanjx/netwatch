namespace NetWatch.Server.Data;

internal sealed class DatabaseInitializer(
    SqliteConnectionFactory _connectionFactory,
    MigrationRunner _migrationRunner,
    ILogger<DatabaseInitializer> _logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_connectionFactory.DatabasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The database path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        await _migrationRunner.RunAsync(cancellationToken);
        _logger.LogInformation("Database initialized at {DatabasePath}", _connectionFactory.DatabasePath);
    }
}
