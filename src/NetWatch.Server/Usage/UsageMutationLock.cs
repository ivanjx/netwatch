namespace NetWatch.Server.Usage;

internal sealed class UsageMutationLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task WaitAsync(CancellationToken cancellationToken) =>
        _semaphore.WaitAsync(cancellationToken);

    public void Release() => _semaphore.Release();
}
