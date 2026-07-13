namespace NetWatch.Core.Usage;

public sealed record UsageTotalsResponse(
    long UploadBytes,
    long DownloadBytes,
    long UploadPackets,
    long DownloadPackets);

public sealed record UsagePeriodResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    UsageTotalsResponse Totals);

public sealed record UsageSummaryResponse(
    DateTimeOffset GeneratedAtUtc,
    string TimeZone,
    string Category,
    string? WanInterface,
    UsagePeriodResponse Today,
    UsagePeriodResponse Week,
    UsagePeriodResponse Month);

public sealed record DeviceUsageResponse(
    string DeviceId,
    string DisplayName,
    UsageTotalsResponse Totals);

