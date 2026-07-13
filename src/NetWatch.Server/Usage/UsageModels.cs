using System.Net;

namespace NetWatch.Server.Usage;

internal static class UsageCategories
{
    public const string Internet = "Internet";
    public const string InternalRouted = "InternalRouted";
}

internal sealed record DeviceAssignment(
    string DeviceId,
    IPAddress? Address,
    IPNetwork? Network,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc);

internal sealed record UsageDelta(
    string DeviceId,
    DateTimeOffset HourStartUtc,
    string Category,
    string WanInterface,
    long UploadBytes,
    long DownloadBytes,
    long UploadPackets,
    long DownloadPackets);

internal sealed record UnresolvedUsageDelta(
    IPAddress Address,
    DateTimeOffset HourStartUtc,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    string Category,
    string WanInterface,
    long UploadBytes,
    long DownloadBytes,
    long UploadPackets,
    long DownloadPackets);

internal sealed record UnresolvedUsageRecord(
    long Id,
    IPAddress Address,
    DateTimeOffset HourStartUtc,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    string Category,
    string WanInterface,
    long UploadBytes,
    long DownloadBytes,
    long UploadPackets,
    long DownloadPackets);

internal sealed record UsageBatch(
    IReadOnlyList<UsageDelta> Attributed,
    IReadOnlyList<UnresolvedUsageDelta> Unresolved)
{
    public bool IsEmpty => Attributed.Count == 0 && Unresolved.Count == 0;
}

internal sealed record UsageQuery(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string Category,
    string? WanInterface);

internal sealed record ReconciledUsage(long UnresolvedId, UsageDelta Delta);

internal sealed record UsageDiagnosticState(
    long UnresolvedBuckets,
    long UnresolvedBytes,
    DateTimeOffset? LastFlushAtUtc,
    DateTimeOffset? LastReconciliationAtUtc);

