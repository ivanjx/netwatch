using System.Net;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

using NetWatch.Core.Usage;
using NetWatch.Server.Common;
using NetWatch.Server.Configuration;
using NetWatch.Server.Data;
using NetWatch.Server.Devices;
using NetWatch.Server.FlowCollection;
using NetWatch.Server.Usage;

namespace NetWatch.IntegrationTests.Usage;

public sealed class UsageTimezoneBoundaryTests
{
    [Fact]
    public async Task Summary_UsesDstTransitionAndCalendarBoundariesForPeriodTotals()
    {
        await WithDatabaseAsync(async (deviceRepository, usageRepository) =>
        {
            var deviceId = await CreateDeviceAsync(deviceRepository);
            await StoreUsageAsync(
                usageRepository,
                new UsageDelta(deviceId, new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero), UsageCategories.Internet, "wan1", 100, 0, 1, 0),
                new UsageDelta(deviceId, new DateTimeOffset(2026, 3, 2, 6, 0, 0, TimeSpan.Zero), UsageCategories.Internet, "wan1", 200, 0, 2, 0),
                new UsageDelta(deviceId, new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero), UsageCategories.Internet, "wan1", 400, 0, 4, 0));

            var nowUtc = new DateTimeOffset(2026, 3, 8, 16, 0, 0, TimeSpan.Zero);
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            var service = new UsageService(
                usageRepository,
                CreateOptions(timeZone),
                new FixedTimeProvider(nowUtc));

            var summary = Assert.IsType<UsageSummaryResult>(
                await service.GetSummaryAsync(null, null, null, CancellationToken.None)).Value;

            Assert.Equal(new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), summary.Today.FromUtc);
            Assert.Equal(new DateTimeOffset(2026, 3, 2, 5, 0, 0, TimeSpan.Zero), summary.Week.FromUtc);
            Assert.Equal(new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.Zero), summary.Month.FromUtc);
            Assert.Equal(400, summary.Today.Totals.UploadBytes);
            Assert.Equal(600, summary.Week.Totals.UploadBytes);
            Assert.Equal(700, summary.Month.Totals.UploadBytes);
        });
    }

    [Fact]
    public async Task Summary_PreservesNonHourUtcOffsetAtLocalMidnight()
    {
        await WithDatabaseAsync(async (deviceRepository, usageRepository) =>
        {
            var deviceId = await CreateDeviceAsync(deviceRepository);
            await StoreUsageAsync(
                usageRepository,
                new UsageDelta(deviceId, new DateTimeOffset(2026, 7, 14, 19, 0, 0, TimeSpan.Zero), UsageCategories.Internet, "wan1", 800, 0, 8, 0));

            var nowUtc = new DateTimeOffset(2026, 7, 14, 20, 0, 0, TimeSpan.Zero);
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
            var service = new UsageService(
                usageRepository,
                CreateOptions(timeZone),
                new FixedTimeProvider(nowUtc));

            var summary = Assert.IsType<UsageSummaryResult>(
                await service.GetSummaryAsync(null, null, null, CancellationToken.None)).Value;

            Assert.Equal(new DateTimeOffset(2026, 7, 14, 18, 15, 0, TimeSpan.Zero), summary.Today.FromUtc);
            Assert.Equal(new DateTimeOffset(2026, 7, 12, 18, 15, 0, TimeSpan.Zero), summary.Week.FromUtc);
            Assert.Equal(new DateTimeOffset(2026, 6, 30, 18, 15, 0, TimeSpan.Zero), summary.Month.FromUtc);
            Assert.Equal(800, summary.Today.Totals.UploadBytes);
        });
    }

    [Fact]
    public async Task History_GroupsLocalCalendarDaysAndFiltersByDevice()
    {
        await WithDatabaseAsync(async (deviceRepository, usageRepository) =>
        {
            var deviceId = await CreateDeviceAsync(deviceRepository);
            await StoreUsageAsync(
                usageRepository,
                new UsageDelta(deviceId, new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero), UsageCategories.Internet, "wan1", 100, 200, 1, 2),
                new UsageDelta(deviceId, new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), UsageCategories.Internet, "wan1", 300, 400, 3, 4),
                new UsageDelta(deviceId, new DateTimeOffset(2026, 3, 9, 5, 0, 0, TimeSpan.Zero), UsageCategories.Internet, "wan1", 500, 600, 5, 6));

            var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            var service = new UsageService(
                usageRepository,
                CreateOptions(timeZone),
                new FixedTimeProvider(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)));

            var history = Assert.IsType<UsageHistoryResult>(await service.GetHistoryAsync(
                "2026-03-08",
                "2026-03-10",
                null,
                "day",
                deviceId,
                null,
                null,
                CancellationToken.None)).Value;

            Assert.Equal(deviceId, history.DeviceId);
            Assert.Equal(new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), history.FromUtc);
            Assert.Equal(2, history.Points.Count);
            Assert.Equal("8 Mar", history.Points[0].Label);
            Assert.Equal(400, history.Points[0].Totals.UploadBytes);
            Assert.Equal(600, history.Points[0].Totals.DownloadBytes);

            var missingDevice = Assert.IsType<UsageHistoryResult>(await service.GetHistoryAsync(
                "2026-03-08",
                "2026-03-10",
                null,
                "day",
                "missing-device",
                null,
                null,
                CancellationToken.None)).Value;
            Assert.Empty(missingDevice.Points);
        });
    }

    private static async Task WithDatabaseAsync(
        Func<DeviceRepository, UsageRepository, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netwatch-{Guid.NewGuid():N}");

        try
        {
            var connectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "netwatch.db"));
            var migrationRunner = new MigrationRunner(connectionFactory, NullLogger<MigrationRunner>.Instance);
            var initializer = new DatabaseInitializer(
                connectionFactory,
                migrationRunner,
                NullLogger<DatabaseInitializer>.Instance);
            await initializer.InitializeAsync(CancellationToken.None);

            await test(
                new DeviceRepository(connectionFactory, NullLogger<DeviceRepository>.Instance),
                new UsageRepository(connectionFactory, NullLogger<UsageRepository>.Instance));
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

    private static async Task<string> CreateDeviceAsync(DeviceRepository repository)
    {
        var result = Assert.IsType<RepositoryResult<string>>(
            await repository.CreateManualAsync(
                "Boundary device",
                "192.168.88.10",
                null,
                "Client",
                null,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CancellationToken.None));
        return result.Value;
    }

    private static async Task StoreUsageAsync(
        UsageRepository repository,
        params UsageDelta[] deltas)
    {
        var result = await repository.FlushAsync(
            new UsageBatch(deltas, []),
            new NetFlowDiagnostics().GetSnapshot(),
            new DateTimeOffset(2026, 7, 14, 20, 0, 0, TimeSpan.Zero),
            CancellationToken.None);
        Assert.IsType<UsageWriteRepositoryResult>(result);
    }

    private static NetWatchOptions CreateOptions(TimeZoneInfo timeZone) => new(
        2055,
        [IPAddress.Loopback],
        null,
        null,
        null,
        false,
        "all",
        [],
        [],
        timeZone,
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(15),
        24,
        30,
        30);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
