using NetWatch.Server.Common;
using NetWatch.Server.MikroTik;

namespace NetWatch.Server.Devices;

internal sealed class DeviceSynchronizationService(
    IMikroTikClient _mikroTikClient,
    DeviceRepository _repository,
    ILogger<DeviceSynchronizationService> _logger)
{
    public async Task<ServiceResult> SynchronizeAsync(CancellationToken cancellationToken)
    {
        var versionResult = await _mikroTikClient.GetRouterVersionAsync(cancellationToken);
        if (versionResult is not RouterVersionResult version)
        {
            return versionResult;
        }

        var detectedAtUtc = DateTimeOffset.UtcNow;
        var storedVersion = await _repository.StoreRouterVersionAsync(
            version.Version,
            version.IsSupported,
            detectedAtUtc,
            cancellationToken);
        if (storedVersion is DeviceRepositoryErrorResult)
        {
            return new DeviceUnavailableServiceErrorResult();
        }

        if (!version.IsSupported)
        {
            return new UnsupportedRouterOsServiceErrorResult(version.Version);
        }

        var leaseResult = await _mikroTikClient.GetDhcpLeasesAsync(cancellationToken);
        if (leaseResult is not DhcpLeasesResult leases)
        {
            return leaseResult;
        }

        var normalized = new List<NormalizedDhcpLease>(leases.Leases.Count);
        foreach (var lease in leases.Leases)
        {
            if (!DeviceIdentity.TryNormalizeMacAddress(lease.MacAddress, out var macAddress) ||
                !DeviceIdentity.TryNormalizeIpAddress(lease.IpAddress, out var ipAddress))
            {
                _logger.LogWarning(
                    "Ignoring DHCP lease with invalid identity data: {MacAddress} {IpAddress}",
                    lease.MacAddress,
                    lease.IpAddress);
                continue;
            }

            normalized.Add(new NormalizedDhcpLease(
                macAddress,
                ipAddress,
                string.IsNullOrWhiteSpace(lease.HostName) ? null : lease.HostName.Trim(),
                lease.Status,
                lease.IsDynamic,
                lease.IsActive,
                lease.LastSeen,
                lease.Server,
                lease.Comment));
        }

        var repositoryResult = await _repository.SynchronizeDhcpLeasesAsync(
            normalized,
            detectedAtUtc,
            cancellationToken);
        return repositoryResult switch
        {
            DeviceWriteRepositoryResult => new DeviceSynchronizationResult(normalized.Count(item => item.IsActive)),
            DeviceRepositoryErrorResult => new DeviceUnavailableServiceErrorResult(),
            _ => new DeviceUnavailableServiceErrorResult()
        };
    }
}
