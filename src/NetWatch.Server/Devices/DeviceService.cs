using System.Globalization;

using NetWatch.Core.Devices;
using NetWatch.Server.Common;
using NetWatch.Server.Configuration;

namespace NetWatch.Server.Devices;

internal sealed class DeviceService(DeviceRepository _repository, NetWatchOptions? _options = null)
{
    public async Task<ServiceResult> ListAsync(CancellationToken cancellationToken) =>
        await _repository.ListAsync(cancellationToken) switch
        {
            RepositoryResult<IReadOnlyList<DeviceSummaryResponse>> success => new DeviceListResult(success.Value),
            DeviceRepositoryErrorResult => new DeviceUnavailableServiceErrorResult(),
            _ => new DeviceUnavailableServiceErrorResult()
        };

    public async Task<ServiceResult> GetAsync(string id, CancellationToken cancellationToken) =>
        await _repository.GetAsync(id, cancellationToken) switch
        {
            RepositoryResult<DeviceDetailsResponse> success => new DeviceDetailsResult(AddReportingTimes(success.Value)),
            DeviceNotFoundRepositoryErrorResult => new DeviceNotFoundServiceErrorResult(),
            DeviceRepositoryErrorResult => new DeviceUnavailableServiceErrorResult(),
            _ => new DeviceUnavailableServiceErrorResult()
        };

    public async Task<ServiceResult> RenameAsync(
        string id,
        RenameDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var friendlyName = NullIfWhiteSpace(request.FriendlyName);
        if (friendlyName?.Length > 100)
        {
            return new InvalidDeviceServiceErrorResult();
        }

        var result = await _repository.RenameAsync(id, friendlyName, DateTimeOffset.UtcNow, cancellationToken);
        return result switch
        {
            DeviceWriteRepositoryResult => await GetAsync(id, cancellationToken),
            DeviceNotFoundRepositoryErrorResult => new DeviceNotFoundServiceErrorResult(),
            DeviceRepositoryErrorResult => new DeviceUnavailableServiceErrorResult(),
            _ => new DeviceUnavailableServiceErrorResult()
        };
    }

    public async Task<ServiceResult> CreateManualAsync(
        CreateManualDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var friendlyName = NullIfWhiteSpace(request.FriendlyName);
        if (friendlyName is null || friendlyName.Length > 100)
        {
            return new InvalidDeviceServiceErrorResult();
        }

        if (!DeviceIdentity.TryNormalizeAddressOrSubnet(request.IpAddressOrSubnet, out var addressOrSubnet))
        {
            return new InvalidDeviceServiceErrorResult();
        }

        string? macAddress = null;
        if (!string.IsNullOrWhiteSpace(request.MacAddress) &&
            !DeviceIdentity.TryNormalizeMacAddress(request.MacAddress, out macAddress))
        {
            return new InvalidDeviceServiceErrorResult();
        }

        var type = NullIfWhiteSpace(request.Type) ?? "Client";
        if (type.Length > 50 || request.Notes?.Length > 2_000)
        {
            return new InvalidDeviceServiceErrorResult();
        }

        var result = await _repository.CreateManualAsync(
            friendlyName,
            addressOrSubnet,
            macAddress,
            type,
            NullIfWhiteSpace(request.Notes),
            DateTimeOffset.UtcNow,
            cancellationToken);
        return result switch
        {
            RepositoryResult<string> success => await GetAsync(success.Value, cancellationToken),
            DeviceConflictRepositoryErrorResult => new DeviceConflictServiceErrorResult(),
            DeviceRepositoryErrorResult => new DeviceUnavailableServiceErrorResult(),
            _ => new DeviceUnavailableServiceErrorResult()
        };
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private DeviceDetailsResponse AddReportingTimes(DeviceDetailsResponse device) => device with
    {
        IpAssignments = device.IpAssignments
            .Select(assignment => assignment with
            {
                ValidFromDisplay = FormatReportingTime(assignment.ValidFromUtc),
                ValidToDisplay = assignment.ValidToUtc is { } validToUtc ?
                    FormatReportingTime(validToUtc) :
                    null
            })
            .ToArray()
    };

    private string FormatReportingTime(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, _options?.ReportingTimeZone ?? TimeZoneInfo.Utc)
            .ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture);
}
