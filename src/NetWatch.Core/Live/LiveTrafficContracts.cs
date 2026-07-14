namespace NetWatch.Core.Live;

public sealed record LiveTrafficRateResponse(
    long UploadBitsPerSecond,
    long DownloadBitsPerSecond);

public sealed record DeviceLiveTrafficResponse(
    string DeviceId,
    LiveTrafficRateResponse Rate);

public sealed record LiveTrafficSnapshotResponse(
    DateTimeOffset GeneratedAtUtc,
    string Source,
    LiveTrafficRateResponse Network,
    IReadOnlyList<DeviceLiveTrafficResponse> Devices);
