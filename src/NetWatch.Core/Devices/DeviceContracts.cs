namespace NetWatch.Core.Devices;

public sealed record DeviceSummaryResponse(
    string Id,
    string DisplayName,
    string? FriendlyName,
    string? DiscoveredName,
    string? MacAddress,
    string? CurrentIpAddress,
    string Type,
    bool IsManual,
    bool IsOnline,
    DateTimeOffset UpdatedAtUtc);

public sealed record DeviceDetailsResponse(
    string Id,
    string DisplayName,
    string? FriendlyName,
    string? DiscoveredName,
    string? MacAddress,
    string? CurrentIpAddress,
    string Type,
    string? Notes,
    bool IsManual,
    bool IsOnline,
    bool? IsDhcpDynamic,
    string? DhcpStatus,
    string? DhcpServer,
    string? DhcpComment,
    string? LastSeen,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<IpAssignmentResponse> IpAssignments);

public sealed record IpAssignmentResponse(
    string IpAddress,
    string Source,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    string? ValidFromDisplay = null,
    string? ValidToDisplay = null);

public sealed record RenameDeviceRequest(string? FriendlyName);

public sealed record CreateManualDeviceRequest(
    string FriendlyName,
    string? IpAddressOrSubnet,
    string? MacAddress,
    string Type,
    string? Notes);
