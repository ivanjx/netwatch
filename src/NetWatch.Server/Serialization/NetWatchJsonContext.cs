using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc;

using NetWatch.Core.Diagnostics;
using NetWatch.Core.Devices;
using NetWatch.Server.MikroTik;

namespace NetWatch.Server.Serialization;

[JsonSerializable(typeof(SystemStatusResponse))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(DeviceSummaryResponse))]
[JsonSerializable(typeof(IReadOnlyList<DeviceSummaryResponse>))]
[JsonSerializable(typeof(DeviceDetailsResponse))]
[JsonSerializable(typeof(IReadOnlyList<IpAssignmentResponse>))]
[JsonSerializable(typeof(RenameDeviceRequest))]
[JsonSerializable(typeof(CreateManualDeviceRequest))]
[JsonSerializable(typeof(RouterSystemResourceDto))]
[JsonSerializable(typeof(IReadOnlyList<RouterSystemResourceDto>))]
[JsonSerializable(typeof(IReadOnlyList<RouterDhcpLeaseDto>))]
internal sealed partial class NetWatchJsonContext : JsonSerializerContext;
