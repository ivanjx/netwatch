using System.Globalization;
using System.Net;

using NetWatch.Server.Configuration;
using NetWatch.Server.FlowCollection;

namespace NetWatch.Server.Usage;

internal sealed class UsageAccumulator(
    NetWatchOptions _options,
    DeviceAttributionService _attributionService)
{
    private readonly Dictionary<UsageKey, UsageDelta> _attributed = [];
    private readonly Dictionary<UnresolvedUsageKey, UnresolvedUsageDelta> _unresolved = [];

    public async Task AddAsync(NormalizedFlow flow, CancellationToken cancellationToken)
    {
        if (IsIgnored(flow.SourceAddress) || IsIgnored(flow.DestinationAddress))
        {
            return;
        }

        var observedAtUtc = flow.LastSeenAtUtc;
        var sourceDeviceId = await _attributionService.ResolveAsync(
            flow.SourceAddress,
            observedAtUtc,
            cancellationToken);
        var destinationDeviceId = await _attributionService.ResolveAsync(
            flow.DestinationAddress,
            observedAtUtc,
            cancellationToken);
        var sourceIsLan = sourceDeviceId is not null || IsPrivate(flow.SourceAddress);
        var destinationIsLan = destinationDeviceId is not null || IsPrivate(flow.DestinationAddress);

        if (!sourceIsLan && !destinationIsLan)
        {
            return;
        }

        var category = sourceIsLan && destinationIsLan ?
            UsageCategories.InternalRouted :
            UsageCategories.Internet;
        var hourStartUtc = new DateTimeOffset(
            observedAtUtc.Year,
            observedAtUtc.Month,
            observedAtUtc.Day,
            observedAtUtc.Hour,
            0,
            0,
            TimeSpan.Zero);

        if (sourceIsLan)
        {
            AddEndpoint(
                sourceDeviceId,
                flow.SourceAddress,
                hourStartUtc,
                flow.FirstSeenAtUtc,
                observedAtUtc,
                category,
                flow.OutputInterface.ToString(CultureInfo.InvariantCulture),
                flow.ByteCount,
                0,
                flow.PacketCount,
                0);
        }

        if (destinationIsLan)
        {
            AddEndpoint(
                destinationDeviceId,
                flow.DestinationAddress,
                hourStartUtc,
                flow.FirstSeenAtUtc,
                observedAtUtc,
                category,
                flow.InputInterface.ToString(CultureInfo.InvariantCulture),
                0,
                flow.ByteCount,
                0,
                flow.PacketCount);
        }
    }

    public UsageBatch GetBatch() => new(_attributed.Values.ToArray(), _unresolved.Values.ToArray());

    public void Clear()
    {
        _attributed.Clear();
        _unresolved.Clear();
    }

    private void AddEndpoint(
        string? deviceId,
        IPAddress address,
        DateTimeOffset hourStartUtc,
        DateTimeOffset firstSeenAtUtc,
        DateTimeOffset lastSeenAtUtc,
        string category,
        string wanInterface,
        long uploadBytes,
        long downloadBytes,
        long uploadPackets,
        long downloadPackets)
    {
        if (deviceId is not null)
        {
            var key = new UsageKey(deviceId, hourStartUtc, category, wanInterface);
            _attributed.TryGetValue(key, out var existing);
            _attributed[key] = new UsageDelta(
                deviceId,
                hourStartUtc,
                category,
                wanInterface,
                (existing?.UploadBytes ?? 0) + uploadBytes,
                (existing?.DownloadBytes ?? 0) + downloadBytes,
                (existing?.UploadPackets ?? 0) + uploadPackets,
                (existing?.DownloadPackets ?? 0) + downloadPackets);
            return;
        }

        var unresolvedKey = new UnresolvedUsageKey(address.ToString(), hourStartUtc, category, wanInterface);
        _unresolved.TryGetValue(unresolvedKey, out var unresolved);
        _unresolved[unresolvedKey] = new UnresolvedUsageDelta(
            address,
            hourStartUtc,
            unresolved is null || firstSeenAtUtc < unresolved.FirstSeenAtUtc ?
                firstSeenAtUtc :
                unresolved.FirstSeenAtUtc,
            unresolved is null || lastSeenAtUtc > unresolved.LastSeenAtUtc ?
                lastSeenAtUtc :
                unresolved.LastSeenAtUtc,
            category,
            wanInterface,
            (unresolved?.UploadBytes ?? 0) + uploadBytes,
            (unresolved?.DownloadBytes ?? 0) + downloadBytes,
            (unresolved?.UploadPackets ?? 0) + uploadPackets,
            (unresolved?.DownloadPackets ?? 0) + downloadPackets);
    }

    private bool IsIgnored(IPAddress address) => _options.IgnoredNetworks.Any(network => network.Contains(address));

    private static bool IsPrivate(IPAddress address)
    {
        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (normalized.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = normalized.GetAddressBytes();
        return bytes[0] == 10 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168;
    }

    private sealed record UsageKey(
        string DeviceId,
        DateTimeOffset HourStartUtc,
        string Category,
        string WanInterface);

    private sealed record UnresolvedUsageKey(
        string Address,
        DateTimeOffset HourStartUtc,
        string Category,
        string WanInterface);
}

