using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NetWatch.Core.Diagnostics;
using NetWatch.Core.Live;
using NetWatch.Core.Usage;
using NetWatch.Server.Common;
using NetWatch.Server.Configuration;
using NetWatch.Server.Data;
using NetWatch.Server.Devices;

namespace NetWatch.IntegrationTests.FlowCollection;

public sealed class HostedNetFlowPipelineTests
{
    [Fact]
    public async Task ReceiveNetFlowDatagram_PersistsUsageExposedByHttpApi()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netwatch-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(directory, "netwatch.db");
        var udpPort = GetAvailableUdpPort();

        try
        {
            await using var application = new NetWatchApplication(databasePath, udpPort);
            using var client = application.CreateClient();
            var deviceRepository = application.Services.GetRequiredService<DeviceRepository>();
            var observedAtUtc = DateTimeOffset.UtcNow;
            var device = Assert.IsType<RepositoryResult<string>>(
                await deviceRepository.CreateManualAsync(
                    "Pipeline laptop",
                    "192.168.88.10",
                    null,
                    "Client",
                    null,
                    observedAtUtc.AddMinutes(-1),
                    CancellationToken.None));

            using var sender = new UdpClient(AddressFamily.InterNetwork);
            await WaitForUdpListenerAsync(client, sender, udpPort);
            await sender.SendAsync(
                CreateDatagram(observedAtUtc, 1_500, 15),
                new IPEndPoint(IPAddress.Loopback, udpPort));

            var usage = await WaitForDeviceUsageAsync(client, device.Value, observedAtUtc);

            Assert.Equal("Pipeline laptop", usage.DisplayName);
            Assert.Equal(1_500, usage.Totals.UploadBytes);
            Assert.Equal(0, usage.Totals.DownloadBytes);
            Assert.Equal(15, usage.Totals.UploadPackets);
            Assert.Equal(0, usage.Totals.DownloadPackets);

            var liveTraffic = Assert.IsType<LiveTrafficSnapshotResponse>(
                await client.GetFromJsonAsync<LiveTrafficSnapshotResponse>("/api/live"));
            var deviceLiveTraffic = Assert.Single(liveTraffic.Devices);
            Assert.Equal(device.Value, deviceLiveTraffic.DeviceId);
            Assert.Equal(6_000, deviceLiveTraffic.Rate.UploadBitsPerSecond);
            Assert.Equal(0, deviceLiveTraffic.Rate.DownloadBitsPerSecond);
            Assert.Equal(deviceLiveTraffic.Rate, liveTraffic.Network);
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

    private static async Task WaitForUdpListenerAsync(HttpClient client, UdpClient sender, int udpPort)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var endpoint = new IPEndPoint(IPAddress.Loopback, udpPort);

        while (true)
        {
            await sender.SendAsync(new byte[] { 0, 5, 0 }, endpoint);
            var diagnostics = await client.GetFromJsonAsync<CollectorDiagnosticsResponse>(
                "/api/diagnostics",
                timeout.Token);
            if (diagnostics?.ReceivedDatagrams > 0)
            {
                return;
            }

            await Task.Delay(100, timeout.Token);
        }
    }

    private static async Task<DeviceUsageResponse> WaitForDeviceUsageAsync(
        HttpClient client,
        string deviceId,
        DateTimeOffset observedAtUtc)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var from = Uri.EscapeDataString(observedAtUtc.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(observedAtUtc.AddHours(1).ToString("O"));

        while (true)
        {
            var usage = await client.GetFromJsonAsync<List<DeviceUsageResponse>>(
                $"/api/usage/devices?from={from}&to={to}",
                timeout.Token);
            var deviceUsage = usage?.SingleOrDefault(item => item.DeviceId == deviceId);
            if (deviceUsage is
                {
                    Totals.UploadBytes: 1_500,
                    Totals.UploadPackets: 15
                })
            {
                return deviceUsage;
            }

            await Task.Delay(100, timeout.Token);
        }
    }

    private static byte[] CreateDatagram(DateTimeOffset exportedAtUtc, uint bytes, uint packets)
    {
        const int headerLength = 24;
        const int recordLength = 48;
        const uint systemUptime = 100_000;
        var datagram = new byte[headerLength + recordLength];

        BinaryPrimitives.WriteUInt16BigEndian(datagram, 5);
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(4), systemUptime);
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(8), (uint)exportedAtUtc.ToUnixTimeSeconds());
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(16), 1);

        var record = datagram.AsSpan(headerLength, recordLength);
        IPAddress.Parse("192.168.88.10").GetAddressBytes().CopyTo(record);
        IPAddress.Parse("1.1.1.1").GetAddressBytes().CopyTo(record[4..]);
        BinaryPrimitives.WriteUInt16BigEndian(record[12..], 2);
        BinaryPrimitives.WriteUInt16BigEndian(record[14..], 3);
        BinaryPrimitives.WriteUInt32BigEndian(record[16..], packets);
        BinaryPrimitives.WriteUInt32BigEndian(record[20..], bytes);
        BinaryPrimitives.WriteUInt32BigEndian(record[24..], systemUptime - 1_000);
        BinaryPrimitives.WriteUInt32BigEndian(record[28..], systemUptime);
        BinaryPrimitives.WriteUInt16BigEndian(record[32..], 50_000);
        BinaryPrimitives.WriteUInt16BigEndian(record[34..], 443);
        record[38] = 6;

        return datagram;
    }

    private static int GetAvailableUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
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
