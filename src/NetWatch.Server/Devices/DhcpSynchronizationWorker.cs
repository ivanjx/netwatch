using NetWatch.Server.Common;
using NetWatch.Server.Configuration;
using NetWatch.Server.MikroTik;

namespace NetWatch.Server.Devices;

internal sealed class DhcpSynchronizationWorker(
    DeviceSynchronizationService _synchronizationService,
    NetWatchOptions _options,
    ILogger<DhcpSynchronizationWorker> _logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.MikroTikBaseUrl is null)
        {
            _logger.LogInformation("DHCP synchronization is disabled because MikroTik is not configured");
            return;
        }

        using var timer = new PeriodicTimer(_options.DhcpSyncInterval);
        do
        {
            var result = await _synchronizationService.SynchronizeAsync(stoppingToken);
            switch (result)
            {
                case DeviceSynchronizationResult success:
                    _logger.LogInformation("Synchronized {ActiveLeaseCount} active DHCP leases", success.ActiveLeaseCount);
                    break;
                case ErrorServiceResult error:
                    _logger.LogWarning("DHCP synchronization failed: {Error}", error);
                    break;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
