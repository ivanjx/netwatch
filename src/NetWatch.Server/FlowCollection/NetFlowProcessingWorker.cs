namespace NetWatch.Server.FlowCollection;

internal sealed class NetFlowProcessingWorker(
    NetFlowChannel _channel,
    NetFlowDiagnostics _diagnostics) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var flow in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            _ = flow;
            _diagnostics.RecordProcessedFlow();
        }
    }
}
