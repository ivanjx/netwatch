using System.Net;

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

using NetWatch.Core.Diagnostics;
using NetWatch.Server.Configuration;
using NetWatch.Server.Data;
using NetWatch.Server.Health;

namespace NetWatch.IntegrationTests.Health;

public sealed class SystemStatusWorkflowTests
{
    [Fact]
    public async Task GetAsync_UsesInitializedDatabaseThroughAllResultBoundaries()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netwatch-{Guid.NewGuid():N}");
        var options = CreateOptions();

        try
        {
            var connectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "netwatch.db"));
            var migrationRunner = new MigrationRunner(connectionFactory, NullLogger<MigrationRunner>.Instance);
            var initializer = new DatabaseInitializer(
                connectionFactory,
                migrationRunner,
                NullLogger<DatabaseInitializer>.Instance);
            await initializer.InitializeAsync(CancellationToken.None);

            var repository = new SystemStatusRepository(
                connectionFactory,
                NullLogger<SystemStatusRepository>.Instance);
            var service = new SystemStatusService(
                repository,
                options,
                new ApplicationState(DateTimeOffset.UnixEpoch));

            var result = await SystemStatusHandler.GetAsync(service, CancellationToken.None);

            var httpResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
            var response = Assert.IsType<SystemStatusResponse>(httpResult.Value);
            Assert.Equal("Healthy", response.State);
            Assert.Equal(1, response.SchemaVersion);
            Assert.Equal(DateTimeOffset.UnixEpoch, response.StartedAtUtc);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static NetWatchOptions CreateOptions() =>
        new(
            2055,
            [],
            null,
            null,
            null,
            false,
            "all",
            [],
            [],
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Jakarta"),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(15),
            24,
            30,
            30);
}
