using System.Text.Json.Serialization;

using NetWatch.Server.Common;

namespace NetWatch.Server.MikroTik;

internal sealed record RouterVersionResult(string Version, int Major, bool IsSupported) : SuccessServiceResult;

internal sealed record DhcpLeasesResult(IReadOnlyList<DhcpLease> Leases) : SuccessServiceResult;

internal sealed record MikroTikNotConfiguredServiceErrorResult : ErrorServiceResult;

internal sealed record MikroTikUnavailableServiceErrorResult : ErrorServiceResult;

internal sealed record MikroTikInvalidResponseServiceErrorResult : ErrorServiceResult;

internal sealed record UnsupportedRouterOsServiceErrorResult(string Version) : ErrorServiceResult;

internal sealed record DhcpLease(
    string MacAddress,
    string IpAddress,
    string? HostName,
    string? Status,
    bool IsDynamic,
    string? LastSeen,
    string? Server,
    string? Comment,
    bool IsActive);

internal sealed record RouterSystemResourceDto(
    [property: JsonPropertyName("version")] string? Version);

internal sealed record RouterDhcpLeaseDto(
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("active-address")] string? ActiveAddress,
    [property: JsonPropertyName("mac-address")] string? MacAddress,
    [property: JsonPropertyName("active-mac-address")] string? ActiveMacAddress,
    [property: JsonPropertyName("host-name")] string? HostName,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("dynamic")] string? Dynamic,
    [property: JsonPropertyName("last-seen")] string? LastSeen,
    [property: JsonPropertyName("server")] string? Server,
    [property: JsonPropertyName("comment")] string? Comment);
