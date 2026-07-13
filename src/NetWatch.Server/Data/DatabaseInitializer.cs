namespace NetWatch.Server.Data;

internal sealed class DatabaseInitializer(
    SqliteConnectionFactory connectionFactory,
    MigrationRunner migrationRunner,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(connectionFactory.DatabasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The database path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        await migrationRunner.RunAsync(cancellationToken);
        logger.LogInformation("Database initialized at {DatabasePath}", connectionFactory.DatabasePath);
    }
}
