using System.Text.Json.Serialization;

using NetWatch.Server.Data;
using NetWatch.Server.MikroTik;

namespace NetWatch.Server.Serialization;

[JsonSerializable(typeof(SpikeStatusResponse))]
[JsonSerializable(typeof(SqliteSpikeResult))]
[JsonSerializable(typeof(RouterOsSpikeResult))]
[JsonSerializable(typeof(TorchRequest))]
internal sealed partial class NetWatchJsonContext : JsonSerializerContext;

internal sealed record SpikeStatusResponse(
    string Product,
    string DatabasePath,
    string NetFlowEndpoint,
    bool MikroTikConfigured);
