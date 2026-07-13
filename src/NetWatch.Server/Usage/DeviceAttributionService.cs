using System.Net;

using NetWatch.Server.Common;

namespace NetWatch.Server.Usage;

internal sealed class DeviceAttributionService(UsageRepository _repository)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<DeviceAssignment> _assignments = [];
    private DateTimeOffset _refreshAfterUtc;

    public async Task<string?> ResolveAsync(
        IPAddress address,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await RefreshIfNeededAsync(cancellationToken);
        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        foreach (var assignment in _assignments)
        {
            if (observedAtUtc < assignment.ValidFromUtc ||
                assignment.ValidToUtc is { } validToUtc && observedAtUtc >= validToUtc)
            {
                continue;
            }

            if (assignment.Address?.Equals(normalized) == true ||
                assignment.Network?.Contains(normalized) == true)
            {
                return assignment.DeviceId;
            }
        }

        return null;
    }

    public void Invalidate() => _refreshAfterUtc = DateTimeOffset.MinValue;

    private async Task RefreshIfNeededAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _refreshAfterUtc)
        {
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow < _refreshAfterUtc)
            {
                return;
            }

            var result = await _repository.ReadAssignmentsAsync(cancellationToken);
            if (result is RepositoryResult<IReadOnlyList<DeviceAssignment>> success)
            {
                _assignments = success.Value;
                _refreshAfterUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}

