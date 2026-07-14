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
    string? MacAddress,
    UsageTotalsResponse Totals);

public sealed record UsageHistoryPointResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string Label,
    UsageTotalsResponse Totals);

public sealed record UsageHistoryResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string TimeZone,
    string Grouping,
    string? DeviceId,
    IReadOnlyList<UsageHistoryPointResponse> Points);
