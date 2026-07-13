using System.Net;
using System.Net.Sockets;

using NetWatch.Server.Configuration;

namespace NetWatch.Server.FlowCollection;

internal sealed class NetFlowUdpListener(
    NetWatchOptions _options,
    NetFlowPacketProcessor _processor,
    ILogger<NetFlowUdpListener> _logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, _options.NetFlowListenPort));
        _logger.LogInformation(
            "NetFlow listener is ready on {ListenAddress}:{ListenPort}/udp",
            IPAddress.Any,
            _options.NetFlowListenPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await socket.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException exception)
            {
                _logger.LogWarning(
                    exception,
                    "NetFlow receive failed; collection will retry");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            await _processor.ProcessAsync(
                received.RemoteEndPoint.Address,
                received.Buffer,
                stoppingToken);
        }
    }
}
