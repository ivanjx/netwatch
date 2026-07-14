using System.Net;

using NetWatch.Core.Diagnostics;
using NetWatch.Server.Common;
using NetWatch.Server.Configuration;

namespace NetWatch.Server.Health;

internal sealed class SystemStatusService(
    SystemStatusRepository _repository,
    NetWatchOptions _options,
    ApplicationState _applicationState,
    TimeProvider _timeProvider)
{
    public async Task<ServiceResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _repository.GetAsync(cancellationToken);
        return result switch
        {
            RepositoryResult<DatabaseStatus> success => new SystemStatusResult(
                new SystemStatusResponse(
                    "NetWatch",
                    "Healthy",
                    "Ready",
                    success.Value.SchemaVersion,
                    $"{IPAddress.Any}:{_options.NetFlowListenPort}",
                    _options.MikroTikBaseUrl is not null,
                    _options.ReportingTimeZone.Id,
                    TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _options.ReportingTimeZone),
                    _applicationState.StartedAtUtc)),
            DatabaseUnavailableRepositoryErrorResult => new SystemStatusUnavailableServiceErrorResult(),
            _ => new SystemStatusUnavailableServiceErrorResult()
        };
    }
}

internal sealed record ApplicationState(DateTimeOffset StartedAtUtc);
