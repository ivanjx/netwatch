using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NetWatch.Core.Devices;
using NetWatch.Core.Diagnostics;
using NetWatch.Core.Usage;
using NetWatch.Server.Configuration;
using NetWatch.Server.Data;
using NetWatch.Server.FlowCollection;
using NetWatch.Server.Usage;

namespace NetWatch.IntegrationTests.Api;

public sealed class HttpApiWorkflowTests
{
    [Fact]
    public async Task DeviceAndUsageEndpoints_RouteSerializeValidateAndPersist()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netwatch-{Guid.NewGuid():N}");

        try
        {
            await using var application = new NetWatchApplication(
                Path.Combine(directory, "netwatch.db"),
                GetAvailableUdpPort());
            using var client = application.CreateClient();

            using var createResponse = await client.PostAsJsonAsync(
                "/api/devices/manual",
                new CreateManualDeviceRequest(
                    "Office laptop",
                    "192.168.88.10",
                    "AA:BB:CC:DD:EE:FF",
                    "Client",
                    "HTTP workflow"));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            Assert.NotNull(createResponse.Headers.Location);
            var created = Assert.IsType<DeviceDetailsResponse>(
                await createResponse.Content.ReadFromJsonAsync<DeviceDetailsResponse>());

            using var renameResponse = await client.PatchAsJsonAsync(
                $"/api/devices/{created.Id}",
                new RenameDeviceRequest("Family laptop"));
            renameResponse.EnsureSuccessStatusCode();
            var renamed = Assert.IsType<DeviceDetailsResponse>(
                await renameResponse.Content.ReadFromJsonAsync<DeviceDetailsResponse>());
            Assert.Equal("Family laptop", renamed.DisplayName);

            var listed = Assert.IsType<List<DeviceSummaryResponse>>(
                await client.GetFromJsonAsync<List<DeviceSummaryResponse>>("/api/devices"));
            Assert.Equal("Family laptop", Assert.Single(listed).DisplayName);

            var observedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            var repository = application.Services.GetRequiredService<UsageRepository>();
            var diagnostics = application.Services.GetRequiredService<NetFlowDiagnostics>();
            var writeResult = await repository.FlushAsync(
                new UsageBatch(
                    [new UsageDelta(
                        created.Id,
                        new DateTimeOffset(
                            observedAtUtc.UtcDateTime.Date.AddHours(observedAtUtc.Hour),
                            TimeSpan.Zero),
                        UsageCategories.Internet,
                        "wan1",
                        4_000,
                        2_000,
                        40,
                        20)],
                    []),
                diagnostics.GetSnapshot(),
                observedAtUtc,
                CancellationToken.None);
            Assert.IsType<UsageWriteRepositoryResult>(writeResult);

            var from = Uri.EscapeDataString(observedAtUtc.AddHours(-1).ToString("O"));
            var to = Uri.EscapeDataString(observedAtUtc.AddHours(1).ToString("O"));
            var usage = Assert.IsType<List<DeviceUsageResponse>>(
                await client.GetFromJsonAsync<List<DeviceUsageResponse>>(
                    $"/api/usage/devices?from={from}&to={to}&wanInterface=wan1"));
            var deviceUsage = Assert.Single(usage);
            Assert.Equal("AA:BB:CC:DD:EE:FF", deviceUsage.MacAddress);
            Assert.Equal(4_000, deviceUsage.Totals.UploadBytes);
            Assert.Equal(2_000, deviceUsage.Totals.DownloadBytes);

            using var clearDeviceUsage = await client.DeleteAsync($"/api/usage/devices/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, clearDeviceUsage.StatusCode);
            usage = Assert.IsType<List<DeviceUsageResponse>>(
                await client.GetFromJsonAsync<List<DeviceUsageResponse>>(
                    $"/api/usage/devices?from={from}&to={to}&wanInterface=wan1"));
            deviceUsage = Assert.Single(usage);
            Assert.Equal(0, deviceUsage.Totals.UploadBytes);
            Assert.Equal(0, deviceUsage.Totals.DownloadBytes);

            writeResult = await repository.FlushAsync(
                new UsageBatch(
                    [new UsageDelta(
                        created.Id,
                        new DateTimeOffset(
                            observedAtUtc.UtcDateTime.Date.AddHours(observedAtUtc.Hour),
                            TimeSpan.Zero),
                        UsageCategories.Internet,
                        "wan1",
                        1_000,
                        500,
                        10,
                        5)],
                    []),
                diagnostics.GetSnapshot(),
                observedAtUtc,
                CancellationToken.None);
            Assert.IsType<UsageWriteRepositoryResult>(writeResult);

            using var clearAllUsage = await client.DeleteAsync("/api/usage");
            Assert.Equal(HttpStatusCode.NoContent, clearAllUsage.StatusCode);
            usage = Assert.IsType<List<DeviceUsageResponse>>(
                await client.GetFromJsonAsync<List<DeviceUsageResponse>>(
                    $"/api/usage/devices?from={from}&to={to}&wanInterface=wan1"));
            deviceUsage = Assert.Single(usage);
            Assert.Equal(0, deviceUsage.Totals.UploadBytes);
            Assert.Equal(0, deviceUsage.Totals.DownloadBytes);

            using var invalidUsage = await client.GetAsync("/api/usage/summary?timeZone=Not/AZone");
            Assert.Equal(HttpStatusCode.BadRequest, invalidUsage.StatusCode);
            var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                await invalidUsage.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>());
            Assert.Null(problem.Detail);

            using var missingDevice = await client.GetAsync("/api/devices/missing");
            Assert.Equal(HttpStatusCode.NotFound, missingDevice.StatusCode);
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

    private sealed class NetWatchApplication(string databasePath, int udpPort) : WebApplicationFactory<Program>
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
                    null,
                    null,
                    null,
                    false,
                    "all",
                    [],
                    [],
                    TimeZoneInfo.Utc,
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(15),
                    24,
                    30,
                    30));
            });
        }
    }
}
