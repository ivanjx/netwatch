using System.Net;

using NetWatch.Server.Configuration;

namespace NetWatch.Server.FlowCollection;

internal sealed class NetFlowPacketProcessor(
    NetWatchOptions _options,
    NetFlowChannel _channel,
    NetFlowDiagnostics _diagnostics,
    ILogger<NetFlowPacketProcessor> _logger)
{
    private const uint UptimeWrapWindowMilliseconds = 86_400_000;

    private readonly Dictionary<IPAddress, ExporterState> _exporterStates = [];
    private readonly object _stateLock = new();

    public async ValueTask ProcessAsync(
        IPAddress exporterAddress,
        ReadOnlyMemory<byte> datagramBytes,
        CancellationToken cancellationToken)
    {
        _diagnostics.RecordReceivedDatagram();
        exporterAddress = NormalizeAddress(exporterAddress);

        if (!IsAllowed(exporterAddress))
        {
            _diagnostics.RecordRejectedExporter();
            _logger.LogWarning("Rejected NetFlow datagram from unapproved exporter {Exporter}", exporterAddress);
            return;
        }

        if (!NetFlowV5Decoder.TryDecode(datagramBytes.Span, out var datagram, out var error))
        {
            RecordMalformed(exporterAddress, error!);
            return;
        }

        var decoded = datagram!;
        if (!TryGetExportedAtUtc(decoded, out var exportedAtUtc, out error))
        {
            RecordMalformed(exporterAddress, error!);
            return;
        }

        MonitorExporter(exporterAddress, decoded);
        _diagnostics.RecordDecodedFlows(decoded.Records.Count);

        foreach (var record in decoded.Records)
        {
            var flow = Normalize(exporterAddress, exportedAtUtc, decoded.SystemUptimeMilliseconds, record);
            await _channel.WriteAsync(flow, cancellationToken);
            _diagnostics.RecordEnqueuedFlow(exportedAtUtc);
        }
    }

    private bool IsAllowed(IPAddress exporterAddress) =>
        _options.NetFlowAllowedExporters.Count == 0 ||
        _options.NetFlowAllowedExporters.Any(allowed => NormalizeAddress(allowed).Equals(exporterAddress));

    private void MonitorExporter(
        IPAddress exporterAddress,
        NetFlowV5Datagram datagram)
    {
        uint? missingFlows = null;
        var rebooted = false;

        lock (_stateLock)
        {
            if (!_exporterStates.TryGetValue(exporterAddress, out var state))
            {
                _exporterStates[exporterAddress] = new ExporterState(
                    datagram.SystemUptimeMilliseconds,
                    unchecked(datagram.FlowSequence + (uint)datagram.Records.Count));
                return;
            }

            rebooted = IsUptimeReset(state.SystemUptimeMilliseconds, datagram.SystemUptimeMilliseconds);
            if (!rebooted)
            {
                var difference = unchecked(datagram.FlowSequence - state.NextExpectedSequence);
                if (difference > 0 && difference < 0x8000_0000)
                {
                    missingFlows = difference;
                }
            }

            var nextExpectedSequence = rebooted ?
                unchecked(datagram.FlowSequence + (uint)datagram.Records.Count) :
                GetNextExpectedSequence(state.NextExpectedSequence, datagram.FlowSequence, datagram.Records.Count);
            _exporterStates[exporterAddress] = new ExporterState(
                datagram.SystemUptimeMilliseconds,
                nextExpectedSequence);
        }

        if (rebooted)
        {
            _diagnostics.RecordRouterReboot();
            _logger.LogWarning(
                "Detected NetFlow exporter reboot or uptime reset for {Exporter}; sequence monitoring was reset",
                exporterAddress);
        }
        else if (missingFlows is not null)
        {
            _diagnostics.RecordSequenceGap(missingFlows.Value);
            _logger.LogWarning(
                "NetFlow sequence gap from {Exporter}; approximately {MissingFlows} flow records were lost",
                exporterAddress,
                missingFlows.Value);
        }
    }

    private static uint GetNextExpectedSequence(uint expected, uint received, int recordCount)
    {
        var difference = unchecked(received - expected);
        return difference < 0x8000_0000 ?
            unchecked(received + (uint)recordCount) :
            expected;
    }

    private static bool IsUptimeReset(uint previous, uint current)
    {
        if (current >= previous)
        {
            return false;
        }

        var isNaturalWrap = previous >= uint.MaxValue - UptimeWrapWindowMilliseconds &&
            current <= UptimeWrapWindowMilliseconds;
        return !isNaturalWrap;
    }

    private static bool TryGetExportedAtUtc(
        NetFlowV5Datagram datagram,
        out DateTimeOffset exportedAtUtc,
        out string? error)
    {
        exportedAtUtc = default;
        error = null;

        if (datagram.UnixNanoseconds >= 1_000_000_000)
        {
            error = $"Header contains invalid nanoseconds value {datagram.UnixNanoseconds}.";
            return false;
        }

        exportedAtUtc = DateTimeOffset.FromUnixTimeSeconds(datagram.UnixSeconds)
            .AddTicks(datagram.UnixNanoseconds / 100);
        return true;
    }

    private static NormalizedFlow Normalize(
        IPAddress exporterAddress,
        DateTimeOffset exportedAtUtc,
        uint systemUptimeMilliseconds,
        NetFlowV5Record record) => new(
            exporterAddress,
            exportedAtUtc,
            ToUtcTimestamp(exportedAtUtc, systemUptimeMilliseconds, record.FirstSeenUptimeMilliseconds),
            ToUtcTimestamp(exportedAtUtc, systemUptimeMilliseconds, record.LastSeenUptimeMilliseconds),
            record.SourceAddress,
            record.DestinationAddress,
            record.InputInterface,
            record.OutputInterface,
            record.PacketCount,
            record.ByteCount,
            record.SourcePort,
            record.DestinationPort,
            record.Protocol);

    private static DateTimeOffset ToUtcTimestamp(
        DateTimeOffset exportedAtUtc,
        uint systemUptimeMilliseconds,
        uint recordUptimeMilliseconds) =>
        exportedAtUtc.AddMilliseconds(-unchecked(systemUptimeMilliseconds - recordUptimeMilliseconds));

    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private void RecordMalformed(IPAddress exporterAddress, string error)
    {
        _diagnostics.RecordMalformedDatagram();
        _logger.LogWarning(
            "Rejected malformed NetFlow datagram from {Exporter}: {Error}",
            exporterAddress,
            error);
    }

    private sealed record ExporterState(
        uint SystemUptimeMilliseconds,
        uint NextExpectedSequence);
}
