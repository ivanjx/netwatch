using NetWatch.Core.Diagnostics;
using NetWatch.Server.Common;

namespace NetWatch.Server.Health;

internal sealed record DatabaseStatus(long SchemaVersion);

internal sealed record DatabaseUnavailableRepositoryErrorResult(string Detail) :
    ErrorRepositoryResult("database_unavailable", "The database status could not be read.");

internal sealed record SystemStatusResult(SystemStatusResponse Value) : SuccessServiceResult;

internal sealed record SystemStatusUnavailableServiceErrorResult(string Detail) :
    ErrorServiceResult("status_unavailable", "NetWatch status is temporarily unavailable.");
