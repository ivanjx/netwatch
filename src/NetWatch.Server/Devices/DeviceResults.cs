using NetWatch.Core.Devices;
using NetWatch.Server.Common;

namespace NetWatch.Server.Devices;

internal sealed record DeviceListResult(IReadOnlyList<DeviceSummaryResponse> Value) : SuccessServiceResult;

internal sealed record DeviceDetailsResult(DeviceDetailsResponse Value) : SuccessServiceResult;

internal sealed record DeviceSynchronizationResult(int ActiveLeaseCount) : SuccessServiceResult;

internal sealed record DeviceNotFoundServiceErrorResult : ErrorServiceResult;

internal sealed record InvalidDeviceServiceErrorResult : ErrorServiceResult;

internal sealed record DeviceConflictServiceErrorResult : ErrorServiceResult;

internal sealed record DeviceUnavailableServiceErrorResult : ErrorServiceResult;

internal sealed record DeviceNotFoundRepositoryErrorResult : ErrorRepositoryResult;

internal sealed record DeviceConflictRepositoryErrorResult : ErrorRepositoryResult;

internal sealed record DeviceRepositoryErrorResult : ErrorRepositoryResult;

internal sealed record DeviceWriteRepositoryResult() : SuccessRepositoryResult;

internal sealed record RouterVersionStoredRepositoryResult() : SuccessRepositoryResult;

internal sealed record NormalizedDhcpLease(
    string MacAddress,
    string IpAddress,
    string? HostName,
    string? Status,
    bool IsDynamic,
    bool IsActive,
    string? LastSeen,
    string? Server,
    string? Comment);
