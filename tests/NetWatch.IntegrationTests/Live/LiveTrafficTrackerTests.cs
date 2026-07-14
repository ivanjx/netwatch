using System.Net;

using NetWatch.Server.Configuration;
using NetWatch.Server.Live;

namespace NetWatch.IntegrationTests.Live;

public sealed class LiveTrafficTrackerTests
{
    private static readonly DateTimeOffset StartedAtUtc =
        new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetSnapshot_SequentialFlowRecordsDoNotMultiplyTheRate()
    {
        var timeProvider = new MutableTimeProvider(StartedAtUtc.AddSeconds(10));
        var tracker = new LiveTrafficTracker(CreateOptions(), timeProvider);

        tracker.Record(
            "device-a",
            125_000_000,
            0,
            StartedAtUtc,
            StartedAtUtc.AddSeconds(10));
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        tracker.Record(
            "device-a",
            125_000_000,
            0,
            StartedAtUtc.AddSeconds(10),
            StartedAtUtc.AddSeconds(20));

        var snapshot = tracker.GetSnapshot();

        var device = Assert.Single(snapshot.Devices);
        Assert.Equal(100_000_000, device.Rate.UploadBitsPerSecond);
        Assert.Equal(device.Rate, snapshot.Network);
    }

    [Fact]
    public void GetSnapshot_ConcurrentFlowRecordsAddTheirRates()
    {
        var tracker = new LiveTrafficTracker(
            CreateOptions(),
            new MutableTimeProvider(StartedAtUtc.AddSeconds(10)));

        tracker.Record(
            "device-a",
            125_000_000,
            0,
            StartedAtUtc,
            StartedAtUtc.AddSeconds(10));
        tracker.Record(
            "device-a",
            125_000_000,
            0,
            StartedAtUtc,
            StartedAtUtc.AddSeconds(10));

        var device = Assert.Single(tracker.GetSnapshot().Devices);

        Assert.Equal(200_000_000, device.Rate.UploadBitsPerSecond);
    }

    [Fact]
    public void GetSnapshot_ComputesDirectionalDeviceAndNetworkRatesFromTheSameWindow()
    {
        var tracker = new LiveTrafficTracker(
            CreateOptions(),
            new MutableTimeProvider(StartedAtUtc.AddSeconds(10)));

        tracker.Record(
            "device-b",
            50_000_000,
            75_000_000,
            StartedAtUtc,
            StartedAtUtc.AddSeconds(10));
        tracker.Record(
            "device-a",
            125_000_000,
            25_000_000,
            StartedAtUtc,
            StartedAtUtc.AddSeconds(10));

        var snapshot = tracker.GetSnapshot();

        Assert.Collection(
            snapshot.Devices,
            device =>
            {
                Assert.Equal("device-a", device.DeviceId);
                Assert.Equal(100_000_000, device.Rate.UploadBitsPerSecond);
                Assert.Equal(20_000_000, device.Rate.DownloadBitsPerSecond);
            },
            device =>
            {
                Assert.Equal("device-b", device.DeviceId);
                Assert.Equal(40_000_000, device.Rate.UploadBitsPerSecond);
                Assert.Equal(60_000_000, device.Rate.DownloadBitsPerSecond);
            });
        Assert.Equal(140_000_000, snapshot.Network.UploadBitsPerSecond);
        Assert.Equal(80_000_000, snapshot.Network.DownloadBitsPerSecond);
    }

    [Fact]
    public void GetSnapshot_UsesTheMinimumSampleIntervalForShortFlows()
    {
        var tracker = new LiveTrafficTracker(
            CreateOptions(),
            new MutableTimeProvider(StartedAtUtc.AddSeconds(1)));

        tracker.Record(
            "device-a",
            1_500,
            0,
            StartedAtUtc,
            StartedAtUtc.AddSeconds(1));

        var device = Assert.Single(tracker.GetSnapshot().Devices);

        Assert.Equal(6_000, device.Rate.UploadBitsPerSecond);
    }

    [Fact]
    public void GetSnapshot_UsesOnlyTheOverlappingPartOfLongFlows()
    {
        var tracker = new LiveTrafficTracker(
            CreateOptions(),
            new MutableTimeProvider(StartedAtUtc.AddSeconds(30)));

        tracker.Record(
            "device-a",
            375_000_000,
            0,
            StartedAtUtc,
            StartedAtUtc.AddSeconds(30));

        var device = Assert.Single(tracker.GetSnapshot().Devices);

        Assert.Equal(100_000_000, device.Rate.UploadBitsPerSecond);
    }

    [Fact]
    public void GetSnapshot_RemovesObservationsAfterTheIdleTimeout()
    {
        var timeProvider = new MutableTimeProvider(StartedAtUtc.AddSeconds(10));
        var tracker = new LiveTrafficTracker(CreateOptions(), timeProvider);
        tracker.Record(
            "device-a",
            125_000_000,
            0,
            StartedAtUtc,
            StartedAtUtc.AddSeconds(10));
        timeProvider.Advance(TimeSpan.FromSeconds(16));

        var snapshot = tracker.GetSnapshot();

        Assert.Empty(snapshot.Devices);
        Assert.Equal(0, snapshot.Network.UploadBitsPerSecond);
        Assert.Equal(0, snapshot.Network.DownloadBitsPerSecond);
    }

    private static NetWatchOptions CreateOptions() => new(
        2055,
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
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(15),
        24,
        30,
        30);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }
}
