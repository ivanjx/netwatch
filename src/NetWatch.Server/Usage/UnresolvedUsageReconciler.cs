using NetWatch.Server.Common;

namespace NetWatch.Server.Usage;

internal sealed class UnresolvedUsageReconciler(
    UsageRepository _repository,
    DeviceAttributionService _attributionService,
    ILogger<UnresolvedUsageReconciler> _logger)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        _attributionService.Invalidate();
        var readResult = await _repository.ReadUnresolvedAsync(1_000, cancellationToken);
        if (readResult is CanceledRepositoryErrorResult)
        {
            return;
        }

        if (readResult is not RepositoryResult<IReadOnlyList<UnresolvedUsageRecord>> success)
        {
            _logger.LogWarning("Unresolved usage reconciliation could not read pending records");
            return;
        }

        var reconciled = new List<ReconciledUsage>();
        foreach (var record in success.Value)
        {
            var deviceId = await _attributionService.ResolveAsync(
                record.Address,
                record.LastSeenAtUtc,
                cancellationToken);
            if (deviceId is null)
            {
                continue;
            }

            reconciled.Add(new ReconciledUsage(
                record.Id,
                new UsageDelta(
                    deviceId,
                    record.HourStartUtc,
                    record.Category,
                    record.WanInterface,
                    record.UploadBytes,
                    record.DownloadBytes,
                    record.UploadPackets,
                    record.DownloadPackets)));
        }

        var storeResult = await _repository.StoreReconciledAsync(
            reconciled,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (storeResult is ErrorRepositoryResult and not CanceledRepositoryErrorResult)
        {
            _logger.LogWarning("Unresolved usage reconciliation could not persist resolved records");
        }
    }
}

