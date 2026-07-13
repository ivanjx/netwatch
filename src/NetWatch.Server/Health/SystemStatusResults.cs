using NetWatch.Core.Diagnostics;
using NetWatch.Server.Common;

namespace NetWatch.Server.Health;

internal sealed record DatabaseStatus(long SchemaVersion);

internal sealed record DatabaseUnavailableRepositoryErrorResult : ErrorRepositoryResult;

internal sealed record SystemStatusResult(SystemStatusResponse Value) : SuccessServiceResult;

internal sealed record SystemStatusUnavailableServiceErrorResult : ErrorServiceResult;
