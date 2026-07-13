namespace NetWatch.Server.FlowCollection;

internal sealed class NetFlowDiagnostics
{
    private long _receivedDatagrams;
    private long _rejectedExporterDatagrams;
    private long _malformedDatagrams;
    private long _decodedFlows;
    private long _enqueuedFlows;
    private long _processedFlows;
    private long _sequenceGapEvents;
    private long _estimatedMissingFlows;
    private long _routerReboots;
    private long _lastFlowAtUtcTicks;
    private readonly Lock _exporterStateLock = new();
    private string? _exporterAddress;
    private uint? _routerUptimeMilliseconds;
    private uint? _netFlowSequence;

    public NetFlowDiagnosticSnapshot GetSnapshot()
    {
        lock (_exporterStateLock)
        {
            return new(
                Interlocked.Read(ref _receivedDatagrams),
                Interlocked.Read(ref _rejectedExporterDatagrams),
                Interlocked.Read(ref _malformedDatagrams),
                Interlocked.Read(ref _decodedFlows),
                Interlocked.Read(ref _enqueuedFlows),
                Interlocked.Read(ref _processedFlows),
                Interlocked.Read(ref _sequenceGapEvents),
                Interlocked.Read(ref _estimatedMissingFlows),
                Interlocked.Read(ref _routerReboots),
                ReadTimestamp(ref _lastFlowAtUtcTicks),
                _exporterAddress,
                _routerUptimeMilliseconds,
                _netFlowSequence);
        }
    }

    public void RecordReceivedDatagram() => Interlocked.Increment(ref _receivedDatagrams);

    public void RecordRejectedExporter() => Interlocked.Increment(ref _rejectedExporterDatagrams);

    public void RecordMalformedDatagram() => Interlocked.Increment(ref _malformedDatagrams);

    public void RecordDecodedFlows(int count) => Interlocked.Add(ref _decodedFlows, count);

    public void RecordEnqueuedFlow(DateTimeOffset exportedAtUtc)
    {
        Interlocked.Increment(ref _enqueuedFlows);
        Interlocked.Exchange(ref _lastFlowAtUtcTicks, exportedAtUtc.UtcTicks);
    }

    public void RecordProcessedFlow() => Interlocked.Increment(ref _processedFlows);

    public void RecordSequenceGap(uint estimatedMissingFlows)
    {
        Interlocked.Increment(ref _sequenceGapEvents);
        Interlocked.Add(ref _estimatedMissingFlows, estimatedMissingFlows);
    }

    public void RecordRouterReboot() => Interlocked.Increment(ref _routerReboots);

    public void RecordExporterState(string exporterAddress, uint routerUptimeMilliseconds, uint netFlowSequence)
    {
        lock (_exporterStateLock)
        {
            _exporterAddress = exporterAddress;
            _routerUptimeMilliseconds = routerUptimeMilliseconds;
            _netFlowSequence = netFlowSequence;
        }
    }

    private static DateTimeOffset? ReadTimestamp(ref long value)
    {
        var ticks = Interlocked.Read(ref value);
        return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}

internal sealed record NetFlowDiagnosticSnapshot(
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
    string? ExporterAddress,
    uint? RouterUptimeMilliseconds,
    uint? NetFlowSequence);
