using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using NetWatch.Core.Devices;
using NetWatch.Server.Configuration;
using NetWatch.Server.Data;

namespace NetWatch.IntegrationTests.Devices;

public sealed class DhcpSynchronizationWorkerTests
{
    [Fact]
    public async Task HostedWorker_RetriesTransientFailureSynchronizesRepeatedlyAndStopsWithHost()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netwatch-{Guid.NewGuid():N}");

        try
        {
            await using var router = await FakeRouterOsServer.StartAsync();
            var application = new NetWatchApplication(
                Path.Combine(directory, "netwatch.db"),
                GetAvailableUdpPort(),
                router.BaseAddress);

            try
            {
                using var client = application.CreateClient();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                DeviceSummaryResponse? synchronized = null;
                while (synchronized is null || router.LeaseRequests < 2)
                {
                    var devices = await client.GetFromJsonAsync<List<DeviceSummaryResponse>>(
                        "/api/devices",
                        timeout.Token);
                    synchronized = devices?.SingleOrDefault();
                    if (synchronized is null || router.LeaseRequests < 2)
                    {
                        await Task.Delay(50, timeout.Token);
                    }
                }

                Assert.Equal("AA:BB:CC:DD:EE:FF", synchronized.MacAddress);
                Assert.Equal("192.168.88.10", synchronized.CurrentIpAddress);
                Assert.True(synchronized.IsOnline);
                Assert.True(router.ResourceRequests >= 3);
                Assert.True(router.LeaseRequests >= 2);
            }
            finally
            {
                await application.DisposeAsync();
            }

            var requestsAfterStop = router.TotalRequests;
            await Task.Delay(300);
            Assert.Equal(requestsAfterStop, router.TotalRequests);
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

    private static int GetAvailableUdpPort()
    {
        using var socket = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private sealed class NetWatchApplication(
        string databasePath,
        int udpPort,
        Uri routerBaseAddress) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SqliteConnectionFactory>();
                services.AddSingleton(new SqliteConnectionFactory(databasePath));
                services.RemoveAll<NetWatchOptions>();
                services.AddSingleton(new NetWatchOptions(
                    udpPort,
                    [IPAddress.Loopback],
                    routerBaseAddress,
                    "netwatch",
                    "password",
                    false,
                    "all",
                    [],
                    [],
                    TimeZoneInfo.Utc,
                    TimeSpan.FromMilliseconds(100),
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(15),
                    24,
                    30,
                    30));
            });
        }
    }

    private sealed class FakeRouterOsServer(WebApplication application) : IAsyncDisposable
    {
        private int _resourceRequests;
        private int _leaseRequests;

        public Uri BaseAddress { get; private set; } = null!;

        public int ResourceRequests => Volatile.Read(ref _resourceRequests);

        public int LeaseRequests => Volatile.Read(ref _leaseRequests);

        public int TotalRequests => ResourceRequests + LeaseRequests;

        public static async Task<FakeRouterOsServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var application = builder.Build();
            var server = new FakeRouterOsServer(application);
            application.MapGet("/rest/system/resource", server.GetResource);
            application.MapGet("/rest/ip/dhcp-server/lease", server.GetLeases);
            await application.StartAsync();

            var addresses = application.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!;
            server.BaseAddress = new Uri(Assert.Single(addresses.Addresses));
            return server;
        }

        public async ValueTask DisposeAsync() => await application.DisposeAsync();

        private IResult GetResource()
        {
            var attempt = Interlocked.Increment(ref _resourceRequests);
            return attempt == 1 ?
                Results.StatusCode(StatusCodes.Status503ServiceUnavailable) :
                Results.Text("{\"version\":\"7.19.3\"}", "application/json");
        }

        private IResult GetLeases()
        {
            Interlocked.Increment(ref _leaseRequests);
            return Results.Text(
                "[{\"active-address\":\"192.168.88.10\",\"active-mac-address\":\"aa-bb-cc-dd-ee-ff\",\"host-name\":\"tablet\",\"status\":\"bound\",\"dynamic\":\"true\",\"server\":\"dhcp1\"}]",
                "application/json");
        }
    }
}
