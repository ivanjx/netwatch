using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

using NetWatch.Server.Common;
using NetWatch.Server.Configuration;
using NetWatch.Server.Data;
using NetWatch.Server.Health;

namespace NetWatch.IntegrationTests;

public sealed class CancellationResultTests
{
    [Fact]
    public async Task SystemStatusCancellation_IsPreservedAcrossResultBoundaries()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netwatch-{Guid.NewGuid():N}");

        try
        {
            var connectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "netwatch.db"));
            var repository = new SystemStatusRepository(
                connectionFactory,
                NullLogger<SystemStatusRepository>.Instance);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.IsType<CanceledRepositoryErrorResult>(
                await repository.GetAsync(cancellation.Token));

            var service = new SystemStatusService(
                repository,
                CreateOptions(),
                new ApplicationState(DateTimeOffset.UnixEpoch),
                TimeProvider.System);

            Assert.IsType<CanceledServiceErrorResult>(
                await service.GetAsync(cancellation.Token));
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
            TimeZoneInfo.Utc,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(15),
            24,
            30,
            30);
}
