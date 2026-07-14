using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc;

using NetWatch.Core.Diagnostics;
using NetWatch.Core.Devices;
using NetWatch.Core.Usage;
using NetWatch.Core.Live;
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
[JsonSerializable(typeof(CollectorDiagnosticsResponse))]
[JsonSerializable(typeof(UsageTotalsResponse))]
[JsonSerializable(typeof(UsagePeriodResponse))]
[JsonSerializable(typeof(UsageSummaryResponse))]
[JsonSerializable(typeof(DeviceUsageResponse))]
[JsonSerializable(typeof(IReadOnlyList<DeviceUsageResponse>))]
[JsonSerializable(typeof(UsageHistoryPointResponse))]
[JsonSerializable(typeof(IReadOnlyList<UsageHistoryPointResponse>))]
[JsonSerializable(typeof(UsageHistoryResponse))]
[JsonSerializable(typeof(LiveTrafficRateResponse))]
[JsonSerializable(typeof(DeviceLiveTrafficResponse))]
[JsonSerializable(typeof(IReadOnlyList<DeviceLiveTrafficResponse>))]
[JsonSerializable(typeof(LiveTrafficSnapshotResponse))]
internal sealed partial class NetWatchJsonContext : JsonSerializerContext;
