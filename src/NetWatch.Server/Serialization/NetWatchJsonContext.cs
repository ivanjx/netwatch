using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc;

using NetWatch.Core.Diagnostics;
using NetWatch.Server.MikroTik;

namespace NetWatch.Server.Serialization;

[JsonSerializable(typeof(SystemStatusResponse))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(RouterOsSpikeResult))]
[JsonSerializable(typeof(TorchRequest))]
internal sealed partial class NetWatchJsonContext : JsonSerializerContext;
