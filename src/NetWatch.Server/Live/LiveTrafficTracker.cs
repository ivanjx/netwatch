using NetWatch.Core.Live;
using NetWatch.Server.Configuration;

namespace NetWatch.Server.Live;

internal sealed class LiveTrafficTracker(
    NetWatchOptions _options,
    TimeProvider _timeProvider)
{
    private readonly Dictionary<string, List<LiveTrafficObservation>> _observations = [];
    private readonly object _sync = new();

    public void Record(
        string deviceId,
        long uploadBytes,
        long downloadBytes,
        DateTimeOffset firstSeenAtUtc,
        DateTimeOffset lastSeenAtUtc)
    {
        var duration = lastSeenAtUtc - firstSeenAtUtc;
        if (duration < _options.LiveSampleInterval)
        {
            duration = _options.LiveSampleInterval;
        }

        var durationSeconds = duration.TotalSeconds;
        var observation = new LiveTrafficObservation(
            _timeProvider.GetUtcNow(),
            ToBitsPerSecond(uploadBytes, durationSeconds),
            ToBitsPerSecond(downloadBytes, durationSeconds));

        lock (_sync)
        {
            if (!_observations.TryGetValue(deviceId, out var deviceObservations))
            {
                deviceObservations = [];
                _observations[deviceId] = deviceObservations;
            }

            deviceObservations.Add(observation);
        }
    }

    public LiveTrafficSnapshotResponse GetSnapshot()
    {
        var generatedAtUtc = _timeProvider.GetUtcNow();
        var expiresBeforeUtc = generatedAtUtc.Subtract(_options.LiveIdleTimeout);
        var devices = new List<DeviceLiveTrafficResponse>();

        lock (_sync)
        {
            foreach (var (deviceId, observations) in _observations.ToArray())
            {
                observations.RemoveAll(observation => observation.RecordedAtUtc < expiresBeforeUtc);
                if (observations.Count == 0)
                {
                    _observations.Remove(deviceId);
                    continue;
                }

                devices.Add(new DeviceLiveTrafficResponse(
                    deviceId,
                    new LiveTrafficRateResponse(
                        observations.Sum(observation => observation.UploadBitsPerSecond),
                        observations.Sum(observation => observation.DownloadBitsPerSecond))));
            }
        }

        devices.Sort((left, right) =>
            string.Compare(left.DeviceId, right.DeviceId, StringComparison.Ordinal));
        return new LiveTrafficSnapshotResponse(
            generatedAtUtc,
            "netflow",
            new LiveTrafficRateResponse(
                devices.Sum(device => device.Rate.UploadBitsPerSecond),
                devices.Sum(device => device.Rate.DownloadBitsPerSecond)),
            devices);
    }

    private static long ToBitsPerSecond(long bytes, double durationSeconds) =>
        (long)Math.Round(bytes * 8d / durationSeconds, MidpointRounding.AwayFromZero);

    private sealed record LiveTrafficObservation(
        DateTimeOffset RecordedAtUtc,
        long UploadBitsPerSecond,
        long DownloadBitsPerSecond);
}
