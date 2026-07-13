namespace NetWatch.Core.Diagnostics;

public sealed record CollectorDiagnosticsResponse(
    long ReceivedDatagrams,
    long RejectedExporterDatagrams,
    long MalformedDatagrams,
    long DecodedFlows,
    long EnqueuedFlows,
    long ProcessedFlows,
    long SequenceGapEvents,
    long EstimatedMissingFlows,
    long RouterReboots,
    DateTimeOffset? LastFlowAtUtc,
    long UnresolvedBuckets,
    long UnresolvedBytes,
    DateTimeOffset? LastFlushAtUtc,
    DateTimeOffset? LastReconciliationAtUtc);

