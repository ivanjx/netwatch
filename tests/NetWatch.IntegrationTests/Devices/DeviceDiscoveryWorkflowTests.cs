using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

using NetWatch.Core.Devices;
using NetWatch.Server.Common;
using NetWatch.Server.Data;
using NetWatch.Server.Devices;
using NetWatch.Server.MikroTik;

namespace NetWatch.IntegrationTests.Devices;

public sealed class DeviceDiscoveryWorkflowTests
{
    [Fact]
    public async Task SynchronizeAndManageDevices_PreservesIdentityHistoryAndFriendlyNames()
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

            var repository = new DeviceRepository(connectionFactory, NullLogger<DeviceRepository>.Instance);
            var router = new FakeMikroTikClient(
                new DhcpLease("aa-bb-cc-dd-ee-ff", "192.168.88.10", "tablet", "bound", true, "10s", "dhcp1", null, true),
                new DhcpLease("aabb.ccdd.eeff", "192.168.88.11", "tablet", "bound", true, "2s", "dhcp1", "Kid's tablet", true));
            var synchronizationService = new DeviceSynchronizationService(
                router,
                repository,
                NullLogger<DeviceSynchronizationService>.Instance);

            Assert.IsType<DeviceSynchronizationResult>(
                await synchronizationService.SynchronizeAsync(CancellationToken.None));
            Assert.IsType<DeviceSynchronizationResult>(
                await synchronizationService.SynchronizeAsync(CancellationToken.None));

            var service = new DeviceService(repository);
            var listResult = await DeviceHandlers.ListAsync(service, CancellationToken.None);
            var devices = Assert.IsAssignableFrom<IReadOnlyList<DeviceSummaryResponse>>(
                Assert.IsAssignableFrom<IValueHttpResult>(listResult).Value);
            var discovered = Assert.Single(devices);
            Assert.Equal("AA:BB:CC:DD:EE:FF", discovered.MacAddress);
            Assert.Equal("192.168.88.11", discovered.CurrentIpAddress);
            Assert.True(discovered.IsOnline);

            var invalidRenameResult = await DeviceHandlers.RenameAsync(
                discovered.Id,
                new RenameDeviceRequest(new string('x', 101)),
                service,
                CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(
                invalidRenameResult).StatusCode);
            Assert.Null(Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                Assert.IsAssignableFrom<IValueHttpResult>(invalidRenameResult).Value).Detail);

            var renameResult = await DeviceHandlers.RenameAsync(
                discovered.Id,
                new RenameDeviceRequest("Family tablet"),
                service,
                CancellationToken.None);
            Assert.Equal("Family tablet", Assert.IsType<DeviceDetailsResponse>(
                Assert.IsAssignableFrom<IValueHttpResult>(renameResult).Value).DisplayName);

            var restartedService = new DeviceService(
                new DeviceRepository(connectionFactory, NullLogger<DeviceRepository>.Instance));
            var detailsResult = await DeviceHandlers.GetAsync(
                discovered.Id,
                restartedService,
                CancellationToken.None);
            var details = Assert.IsType<DeviceDetailsResponse>(
                Assert.IsAssignableFrom<IValueHttpResult>(detailsResult).Value);
            Assert.Equal("Family tablet", details.FriendlyName);
            Assert.Equal(2, details.IpAssignments.Count);
            Assert.Null(details.IpAssignments[0].ValidToUtc);
            Assert.NotNull(details.IpAssignments[1].ValidToUtc);

            var manualResult = await DeviceHandlers.CreateManualAsync(
                new CreateManualDeviceRequest(
                    "Printer subnet",
                    "192.168.90.0/24",
                    null,
                    "Infrastructure",
                    "Static printers"),
                restartedService,
                CancellationToken.None);
            var manual = Assert.IsType<DeviceDetailsResponse>(
                Assert.IsAssignableFrom<IValueHttpResult>(manualResult).Value);
            Assert.True(manual.IsManual);
            Assert.Equal("192.168.90.0/24", manual.CurrentIpAddress);

            router.Version = new RouterVersionResult("6.49.18", 6, false);
            var unsupported = await synchronizationService.SynchronizeAsync(CancellationToken.None);
            Assert.IsType<UnsupportedRouterOsServiceErrorResult>(unsupported);
            Assert.Equal(2, router.LeaseRequestCount);

            await using var connection = await connectionFactory.OpenConnectionAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT router_os_version || ':' || is_supported FROM router_state WHERE singleton_id = 1;";
            Assert.Equal("6.49.18:0", await command.ExecuteScalarAsync(CancellationToken.None));
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

    private sealed class FakeMikroTikClient(params DhcpLease[] _leases) : IMikroTikClient
    {
        private int _nextLease;

        public RouterVersionResult Version { get; set; } = new("7.19.3", 7, true);

        public int LeaseRequestCount { get; private set; }

        public Task<ServiceResult> GetRouterVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ServiceResult>(Version);

        public Task<ServiceResult> GetDhcpLeasesAsync(CancellationToken cancellationToken)
        {
            LeaseRequestCount++;
            var lease = _leases[Math.Min(_nextLease, _leases.Length - 1)];
            _nextLease++;
            return Task.FromResult<ServiceResult>(new DhcpLeasesResult([lease]));
        }
    }
}
