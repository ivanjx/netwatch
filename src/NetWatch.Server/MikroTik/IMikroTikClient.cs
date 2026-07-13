using NetWatch.Server.Common;

namespace NetWatch.Server.MikroTik;

internal interface IMikroTikClient
{
    Task<ServiceResult> GetRouterVersionAsync(CancellationToken cancellationToken);

    Task<ServiceResult> GetDhcpLeasesAsync(CancellationToken cancellationToken);
}
