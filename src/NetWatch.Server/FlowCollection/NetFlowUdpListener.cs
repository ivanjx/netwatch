using System.Net;
using System.Net.Sockets;

using NetWatch.Server.Configuration;

namespace NetWatch.Server.FlowCollection;

internal sealed class NetFlowUdpListener(NetWatchOptions options, ILogger<NetFlowUdpListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, options.NetFlowListenPort));
        logger.LogInformation(
            "NetFlow listener is ready on {ListenAddress}:{ListenPort}/udp",
            IPAddress.Any,
            options.NetFlowListenPort);

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

            if (!NetFlowV5Decoder.TryDecode(received.Buffer, out var datagram, out var error))
            {
                logger.LogWarning(
                    "Rejected NetFlow datagram from {Exporter}: {Error}",
                    received.RemoteEndPoint,
                    error);
                continue;
            }

            foreach (var record in datagram!.Records)
            {
                logger.LogInformation(
                    "NetFlow v5 exporter={Exporter} sequence={Sequence} source={Source}:{SourcePort} destination={Destination}:{DestinationPort} protocol={Protocol} packets={Packets} bytes={Bytes} input={InputInterface} output={OutputInterface}",
                    received.RemoteEndPoint,
                    datagram.FlowSequence,
                    record.SourceAddress,
                    record.SourcePort,
                    record.DestinationAddress,
                    record.DestinationPort,
                    record.Protocol,
                    record.PacketCount,
                    record.ByteCount,
                    record.InputInterface,
                    record.OutputInterface);
            }
        }
    }
}
