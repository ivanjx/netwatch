using System.Net;

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

using NetWatch.Core.Diagnostics;
using NetWatch.Core.Usage;
using NetWatch.Server.Common;
using NetWatch.Server.Configuration;
using NetWatch.Server.Data;
using NetWatch.Server.Devices;
using NetWatch.Server.FlowCollection;
using NetWatch.Server.Live;
using NetWatch.Server.Usage;

namespace NetWatch.IntegrationTests.Usage;

public sealed class UsageAggregationWorkflowTests
{
    [Fact]
    public async Task AggregateAndReconcileFlows_PersistsTimezoneAgnosticUsageAndDiagnostics()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netwatch-{Guid.NewGuid():N}");

        try
        {
            var options = CreateOptions();
            var connectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "netwatch.db"));
            var migrationRunner = new MigrationRunner(connectionFactory, NullLogger<MigrationRunner>.Instance);
            var initializer = new DatabaseInitializer(
                connectionFactory,
                migrationRunner,
                NullLogger<DatabaseInitializer>.Instance);
            await initializer.InitializeAsync(CancellationToken.None);

            var deviceRepository = new DeviceRepository(connectionFactory, NullLogger<DeviceRepository>.Instance);
            var observedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            var knownDevice = Assert.IsType<RepositoryResult<string>>(
                await deviceRepository.CreateManualAsync(
                    "Known laptop",
                    "192.168.88.10",
                    null,
                    "Client",
                    null,
                    observedAtUtc.AddMinutes(-1),
                    CancellationToken.None));
            var usageRepository = new UsageRepository(
                connectionFactory,
                NullLogger<UsageRepository>.Instance);
            var attribution = new DeviceAttributionService(usageRepository);
            var liveTrafficTracker = new LiveTrafficTracker(options, TimeProvider.System);
            var accumulator = new UsageAccumulator(options, attribution, liveTrafficTracker);

            await accumulator.AddAsync(
                CreateFlow("192.168.88.10", "1.1.1.1", 1_000, 10, observedAtUtc),
                CancellationToken.None);
            await accumulator.AddAsync(
                CreateFlow("8.8.8.8", "192.168.88.10", 2_000, 20, observedAtUtc),
                CancellationToken.None);
            await accumulator.AddAsync(
                CreateFlow("192.168.88.20", "9.9.9.9", 3_000, 30, observedAtUtc),
                CancellationToken.None);

            var diagnostics = new NetFlowDiagnostics();
            Assert.IsType<UsageWriteRepositoryResult>(await usageRepository.FlushAsync(
                accumulator.GetBatch(),
                diagnostics.GetSnapshot(),
                DateTimeOffset.UtcNow,
                CancellationToken.None));
            accumulator.Clear();

            var usageService = new UsageService(usageRepository, options, TimeProvider.System);
            var summary = Assert.IsType<UsageSummaryResult>(
                await usageService.GetSummaryAsync(null, null, null, CancellationToken.None)).Value;
            Assert.Equal("America/New_York", summary.TimeZone);
            Assert.Equal(1_000, summary.Today.Totals.UploadBytes);
            Assert.Equal(2_000, summary.Today.Totals.DownloadBytes);
            var newYorkNow = TimeZoneInfo.ConvertTime(summary.GeneratedAtUtc, options.ReportingTimeZone);
            var expectedDayStartUtc = new DateTimeOffset(
                TimeZoneInfo.ConvertTimeToUtc(
                    new DateTime(newYorkNow.Year, newYorkNow.Month, newYorkNow.Day),
                    options.ReportingTimeZone),
                TimeSpan.Zero);
            Assert.Equal(expectedDayStartUtc, summary.Today.FromUtc);

            var diagnosticsService = new CollectorDiagnosticsService(diagnostics, usageRepository);
            var pending = Assert.IsType<CollectorDiagnosticsResult>(
                await diagnosticsService.GetAsync(CancellationToken.None)).Value;
            Assert.Equal(1, pending.UnresolvedBuckets);
            Assert.Equal(3_000, pending.UnresolvedBytes);

            var unresolvedDevice = Assert.IsType<RepositoryResult<string>>(
                await deviceRepository.CreateManualAsync(
                    "Later-known phone",
                    "192.168.88.20",
                    null,
                    "Client",
                    null,
                    observedAtUtc.AddSeconds(-1),
                    CancellationToken.None));
            var reconciler = new UnresolvedUsageReconciler(
                usageRepository,
                attribution,
                NullLogger<UnresolvedUsageReconciler>.Instance);
            await reconciler.ReconcileAsync(CancellationToken.None);

            var restartedService = new UsageService(
                new UsageRepository(connectionFactory, NullLogger<UsageRepository>.Instance),
                options,
                TimeProvider.System);
            var devicesResult = Assert.IsType<DeviceUsageListResult>(await restartedService.GetDevicesAsync(
                observedAtUtc.AddHours(-1).ToString("O"),
                observedAtUtc.AddHours(1).ToString("O"),
                null,
                null,
                null,
                CancellationToken.None));
            var knownUsage = Assert.Single(devicesResult.Value, item => item.DeviceId == knownDevice.Value);
            Assert.Equal(1_000, knownUsage.Totals.UploadBytes);
            Assert.Equal(2_000, knownUsage.Totals.DownloadBytes);
            var reconciledUsage = Assert.Single(
                devicesResult.Value,
                item => item.DeviceId == unresolvedDevice.Value);
            Assert.Equal(3_000, reconciledUsage.Totals.UploadBytes);

            var reconciledDiagnostics = Assert.IsType<CollectorDiagnosticsResult>(
                await diagnosticsService.GetAsync(CancellationToken.None)).Value;
            Assert.Equal(0, reconciledDiagnostics.UnresolvedBuckets);
            Assert.NotNull(reconciledDiagnostics.LastReconciliationAtUtc);

            await using var connection = await connectionFactory.OpenConnectionAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'usage_daily';";
            Assert.Equal(0L, await command.ExecuteScalarAsync(CancellationToken.None));
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

    private static NormalizedFlow CreateFlow(
        string source,
        string destination,
        uint bytes,
        uint packets,
        DateTimeOffset observedAtUtc) => new(
            IPAddress.Parse("192.168.88.1"),
            observedAtUtc,
            observedAtUtc.AddSeconds(-10),
            observedAtUtc,
            IPAddress.Parse(source),
            IPAddress.Parse(destination),
            2,
            3,
            packets,
            bytes,
            1234,
            443,
            6);

    private static NetWatchOptions CreateOptions() => new(
        2055,
        [IPAddress.Parse("192.168.88.1")],
        null,
        null,
        null,
        false,
        "bridge",
        [],
        [],
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York"),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(15),
        24,
        30,
        30);
}
