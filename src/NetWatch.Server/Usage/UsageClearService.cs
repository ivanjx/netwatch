using NetWatch.Server.Common;

namespace NetWatch.Server.Usage;

internal sealed class UsageClearService(
    UsageRepository _repository,
    UsageAccumulator _accumulator,
    UsageMutationLock _mutationLock)
{
    public async Task<ServiceResult> ClearDeviceAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mutationLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledServiceErrorResult();
        }

        try
        {
            var result = await _repository.ClearDeviceAsync(deviceId, cancellationToken);
            if (result is UsageWriteRepositoryResult)
            {
                _accumulator.ClearDevice(deviceId);
            }

            return Map(result);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<ServiceResult> ClearAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _mutationLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledServiceErrorResult();
        }

        try
        {
            var result = await _repository.ClearAllAsync(cancellationToken);
            if (result is UsageWriteRepositoryResult)
            {
                _accumulator.Clear();
            }

            return Map(result);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private static ServiceResult Map(RepositoryResult result) => result switch
    {
        UsageWriteRepositoryResult => new UsageClearedResult(),
        CanceledRepositoryErrorResult => new CanceledServiceErrorResult(),
        ErrorRepositoryResult => new UsageUnavailableServiceErrorResult(),
        _ => new UsageUnavailableServiceErrorResult()
    };
}
