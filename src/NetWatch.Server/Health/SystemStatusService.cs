using System.Net;

using NetWatch.Core.Diagnostics;
using NetWatch.Server.Common;
using NetWatch.Server.Configuration;

namespace NetWatch.Server.Health;

internal sealed class SystemStatusService(
    SystemStatusRepository repository,
    NetWatchOptions options,
    ApplicationState applicationState)
{
    public async Task<ServiceResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await repository.GetAsync(cancellationToken);
        return result switch
        {
            RepositoryResult<DatabaseStatus> success => new SystemStatusResult(
                new SystemStatusResponse(
                    "NetWatch",
                    "Healthy",
                    "Ready",
                    success.Value.SchemaVersion,
                    $"{IPAddress.Any}:{options.NetFlowListenPort}",
                    options.MikroTikBaseUrl is not null,
                    options.ReportingTimeZone.Id,
                    applicationState.StartedAtUtc)),
            DatabaseUnavailableRepositoryErrorResult error =>
                new SystemStatusUnavailableServiceErrorResult(error.Detail),
            _ => new SystemStatusUnavailableServiceErrorResult("Unexpected repository result.")
        };
    }
}

internal sealed record ApplicationState(DateTimeOffset StartedAtUtc);
