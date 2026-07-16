using NetWatch.Server.Common;
using NetWatch.Server.Configuration;
using NetWatch.Server.Usage;

namespace NetWatch.Server.FlowCollection;

internal sealed class NetFlowProcessingWorker(
    NetFlowChannel _channel,
    NetFlowDiagnostics _diagnostics,
    UsageAccumulator _accumulator,
    UsageRepository _repository,
    UnresolvedUsageReconciler _reconciler,
    UsageMutationLock _usageMutationLock,
    NetWatchOptions _options,
    ILogger<NetFlowProcessingWorker> _logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextFlushAtUtc = DateTimeOffset.UtcNow.Add(_options.UsageFlushInterval);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                while (_channel.Reader.TryRead(out var flow))
                {
                    await _accumulator.AddAsync(flow, stoppingToken);
                    _diagnostics.RecordProcessedFlow();
                    if (DateTimeOffset.UtcNow >= nextFlushAtUtc)
                    {
                        break;
                    }
                }

                if (DateTimeOffset.UtcNow >= nextFlushAtUtc)
                {
                    await FlushAndReconcileAsync(stoppingToken);
                    nextFlushAtUtc = DateTimeOffset.UtcNow.Add(_options.UsageFlushInterval);
                    continue;
                }

                var waitDuration = nextFlushAtUtc - DateTimeOffset.UtcNow;
                if (waitDuration <= TimeSpan.Zero)
                {
                    continue;
                }

                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                waitCancellation.CancelAfter(waitDuration);
                try
                {
                    if (!await _channel.Reader.WaitToReadAsync(waitCancellation.Token))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await FlushAndReconcileAsync(CancellationToken.None);
        }
    }

    private async Task FlushAndReconcileAsync(CancellationToken cancellationToken)
    {
        await _usageMutationLock.WaitAsync(cancellationToken);
        try
        {
            var batch = _accumulator.GetBatch();
            var result = await _repository.FlushAsync(
                batch,
                _diagnostics.GetSnapshot(),
                DateTimeOffset.UtcNow,
                cancellationToken);
            if (result is UsageWriteRepositoryResult)
            {
                _accumulator.Clear();
            }
            else if (result is CanceledRepositoryErrorResult)
            {
                return;
            }
            else if (result is ErrorRepositoryResult)
            {
                _logger.LogWarning("Usage aggregates remain queued after a failed flush");
                return;
            }

            await _reconciler.ReconcileAsync(cancellationToken);
        }
        finally
        {
            _usageMutationLock.Release();
        }
    }
}
