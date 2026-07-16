using NetWatch.Core.Diagnostics;
using NetWatch.Server.Common;
using NetWatch.Server.Usage;

namespace NetWatch.Server.FlowCollection;

internal sealed class CollectorDiagnosticsService(
    NetFlowDiagnostics _diagnostics,
    UsageRepository _usageRepository)
{
    public async Task<ServiceResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _usageRepository.GetDiagnosticStateAsync(cancellationToken);
        if (result is CanceledRepositoryErrorResult)
        {
            return new CanceledServiceErrorResult();
        }

        if (result is not RepositoryResult<UsageDiagnosticState> usage)
        {
            return new CollectorDiagnosticsUnavailableServiceErrorResult();
        }

        var collector = _diagnostics.GetSnapshot();
        return new CollectorDiagnosticsResult(new CollectorDiagnosticsResponse(
            collector.ReceivedDatagrams,
            collector.RejectedExporterDatagrams,
            collector.MalformedDatagrams,
            collector.DecodedFlows,
            collector.EnqueuedFlows,
            collector.ProcessedFlows,
            collector.SequenceGapEvents,
            collector.EstimatedMissingFlows,
            collector.RouterReboots,
            collector.LastFlowAtUtc,
            usage.Value.UnresolvedBuckets,
            usage.Value.UnresolvedBytes,
            usage.Value.LastFlushAtUtc,
            usage.Value.LastReconciliationAtUtc));
    }
}

internal sealed record CollectorDiagnosticsResult(CollectorDiagnosticsResponse Value) : SuccessServiceResult;

internal sealed record CollectorDiagnosticsUnavailableServiceErrorResult : ErrorServiceResult;

