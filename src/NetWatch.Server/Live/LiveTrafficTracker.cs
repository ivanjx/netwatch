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
            firstSeenAtUtc = lastSeenAtUtc.Subtract(_options.LiveSampleInterval);
        }

        var observation = new LiveTrafficObservation(
            _timeProvider.GetUtcNow(),
            firstSeenAtUtc,
            lastSeenAtUtc,
            uploadBytes,
            downloadBytes);

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
                }
            }

            if (_observations.Count > 0)
            {
                var allObservations = _observations.Values.SelectMany(item => item).ToArray();
                var windowEndUtc = allObservations.Max(observation => observation.LastSeenAtUtc);
                var windowStartUtc = windowEndUtc.Subtract(_options.LiveIdleTimeout);
                var earliestObservationUtc = allObservations.Min(observation => observation.FirstSeenAtUtc);
                if (earliestObservationUtc > windowStartUtc)
                {
                    windowStartUtc = earliestObservationUtc;
                }

                var windowDurationSeconds = (windowEndUtc - windowStartUtc).TotalSeconds;
                foreach (var (deviceId, observations) in _observations)
                {
                    devices.Add(new DeviceLiveTrafficResponse(
                        deviceId,
                        new LiveTrafficRateResponse(
                            ToBitsPerSecond(
                                observations,
                                observation => observation.UploadBytes,
                                windowStartUtc,
                                windowEndUtc,
                                windowDurationSeconds),
                            ToBitsPerSecond(
                                observations,
                                observation => observation.DownloadBytes,
                                windowStartUtc,
                                windowEndUtc,
                                windowDurationSeconds))));
                }
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

    private static long ToBitsPerSecond(
        IReadOnlyList<LiveTrafficObservation> observations,
        Func<LiveTrafficObservation, long> selectBytes,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        double windowDurationSeconds)
    {
        var bits = 0d;
        foreach (var observation in observations)
        {
            var overlapStartUtc = observation.FirstSeenAtUtc > windowStartUtc ?
                observation.FirstSeenAtUtc :
                windowStartUtc;
            var overlapEndUtc = observation.LastSeenAtUtc < windowEndUtc ?
                observation.LastSeenAtUtc :
                windowEndUtc;
            if (overlapEndUtc <= overlapStartUtc)
            {
                continue;
            }

            var observationDurationTicks = (observation.LastSeenAtUtc - observation.FirstSeenAtUtc).Ticks;
            var overlapDurationTicks = (overlapEndUtc - overlapStartUtc).Ticks;
            bits += selectBytes(observation) * 8d * overlapDurationTicks / observationDurationTicks;
        }

        return (long)Math.Round(bits / windowDurationSeconds, MidpointRounding.AwayFromZero);
    }

    private sealed record LiveTrafficObservation(
        DateTimeOffset RecordedAtUtc,
        DateTimeOffset FirstSeenAtUtc,
        DateTimeOffset LastSeenAtUtc,
        long UploadBytes,
        long DownloadBytes);
}
