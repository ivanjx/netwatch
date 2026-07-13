using System.Buffers.Binary;
using System.Net;

using Microsoft.Extensions.Logging.Abstractions;

using NetWatch.Server.Configuration;
using NetWatch.Server.FlowCollection;

namespace NetWatch.IntegrationTests.FlowCollection;

public sealed class NetFlowCollectionWorkflowTests
{
    [Fact]
    public async Task ProcessDatagrams_NormalizesAllFlowsAndReportsLossAndReboot()
    {
        var exporter = IPAddress.Parse("192.168.88.1");
        var channel = new NetFlowChannel();
        var diagnostics = new NetFlowDiagnostics();
        var processor = CreateProcessor(exporter, channel, diagnostics);

        await processor.ProcessAsync(
            exporter.MapToIPv6(),
            CreateDatagram(
                systemUptime: 100_000,
                unixSeconds: 1_700_000_000,
                unixNanoseconds: 500_000_000,
                sequence: 100,
                new TestRecord(
                    "192.168.88.10",
                    "1.1.1.1",
                    4,
                    7,
                    10,
                    1_500,
                    90_000,
                    95_000,
                    50_000,
                    443,
                    6),
                new TestRecord(
                    "8.8.8.8",
                    "192.168.88.20",
                    7,
                    4,
                    2,
                    300,
                    98_000,
                    99_000,
                    53,
                    53,
                    17)),
            CancellationToken.None);

        var first = await channel.Reader.ReadAsync(CancellationToken.None);
        var second = await channel.Reader.ReadAsync(CancellationToken.None);
        var exportedAtUtc = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddMilliseconds(500);

        Assert.Equal(exporter, first.ExporterAddress);
        Assert.Equal(exportedAtUtc, first.ExportedAtUtc);
        Assert.Equal(exportedAtUtc.AddSeconds(-10), first.FirstSeenAtUtc);
        Assert.Equal(exportedAtUtc.AddSeconds(-5), first.LastSeenAtUtc);
        Assert.Equal(IPAddress.Parse("192.168.88.10"), first.SourceAddress);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), first.DestinationAddress);
        Assert.Equal((ushort)4, first.InputInterface);
        Assert.Equal((ushort)7, first.OutputInterface);
        Assert.Equal((uint)10, first.PacketCount);
        Assert.Equal((uint)1_500, first.ByteCount);
        Assert.Equal((ushort)50_000, first.SourcePort);
        Assert.Equal((ushort)443, first.DestinationPort);
        Assert.Equal((byte)6, first.Protocol);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), second.SourceAddress);

        await processor.ProcessAsync(
            exporter,
            CreateDatagram(101_000, 1_700_000_001, 0, 105, DefaultRecord),
            CancellationToken.None);
        await processor.ProcessAsync(
            exporter,
            CreateDatagram(500, 1_700_000_002, 0, 1, DefaultRecord),
            CancellationToken.None);

        var snapshot = diagnostics.GetSnapshot();
        Assert.Equal(3, snapshot.ReceivedDatagrams);
        Assert.Equal(4, snapshot.DecodedFlows);
        Assert.Equal(4, snapshot.EnqueuedFlows);
        Assert.Equal(1, snapshot.SequenceGapEvents);
        Assert.Equal(3, snapshot.EstimatedMissingFlows);
        Assert.Equal(1, snapshot.RouterReboots);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_002), snapshot.LastFlowAtUtc);
    }

    [Fact]
    public async Task ProcessDatagrams_RejectsUnapprovedAndMalformedPacketsWithoutStopping()
    {
        var exporter = IPAddress.Parse("192.168.88.1");
        var channel = new NetFlowChannel();
        var diagnostics = new NetFlowDiagnostics();
        var processor = CreateProcessor(exporter, channel, diagnostics);

        await processor.ProcessAsync(
            IPAddress.Parse("192.168.88.2"),
            CreateDatagram(1_000, 1_700_000_000, 0, 1, DefaultRecord),
            CancellationToken.None);
        await processor.ProcessAsync(exporter, new byte[] { 0, 5, 0 }, CancellationToken.None);
        await processor.ProcessAsync(
            exporter,
            CreateDatagram(1_000, 1_700_000_000, 1_000_000_000, 1, DefaultRecord),
            CancellationToken.None);
        await processor.ProcessAsync(
            exporter,
            CreateDatagram(1_000, 1_700_000_000, 0, 1, DefaultRecord),
            CancellationToken.None);

        var flow = await channel.Reader.ReadAsync(CancellationToken.None);
        Assert.Equal(IPAddress.Parse(DefaultRecord.SourceAddress), flow.SourceAddress);

        var snapshot = diagnostics.GetSnapshot();
        Assert.Equal(4, snapshot.ReceivedDatagrams);
        Assert.Equal(1, snapshot.RejectedExporterDatagrams);
        Assert.Equal(2, snapshot.MalformedDatagrams);
        Assert.Equal(1, snapshot.DecodedFlows);
        Assert.Equal(1, snapshot.EnqueuedFlows);
    }

    private static NetFlowPacketProcessor CreateProcessor(
        IPAddress allowedExporter,
        NetFlowChannel channel,
        NetFlowDiagnostics diagnostics) => new(
            CreateOptions(allowedExporter),
            channel,
            diagnostics,
            NullLogger<NetFlowPacketProcessor>.Instance);

    private static NetWatchOptions CreateOptions(IPAddress allowedExporter) => new(
        2055,
        [allowedExporter],
        null,
        null,
        null,
        false,
        "all",
        [],
        [],
        TimeZoneInfo.Utc,
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(15),
        24,
        30,
        30);

    private static byte[] CreateDatagram(
        uint systemUptime,
        uint unixSeconds,
        uint unixNanoseconds,
        uint sequence,
        params TestRecord[] records)
    {
        const int headerLength = 24;
        const int recordLength = 48;
        var datagram = new byte[headerLength + (records.Length * recordLength)];
        BinaryPrimitives.WriteUInt16BigEndian(datagram, 5);
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(2), (ushort)records.Length);
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(4), systemUptime);
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(8), unixSeconds);
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(12), unixNanoseconds);
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(16), sequence);

        for (var index = 0; index < records.Length; index++)
        {
            var target = datagram.AsSpan(headerLength + (index * recordLength), recordLength);
            var record = records[index];
            IPAddress.Parse(record.SourceAddress).GetAddressBytes().CopyTo(target);
            IPAddress.Parse(record.DestinationAddress).GetAddressBytes().CopyTo(target[4..]);
            BinaryPrimitives.WriteUInt16BigEndian(target[12..], record.InputInterface);
            BinaryPrimitives.WriteUInt16BigEndian(target[14..], record.OutputInterface);
            BinaryPrimitives.WriteUInt32BigEndian(target[16..], record.PacketCount);
            BinaryPrimitives.WriteUInt32BigEndian(target[20..], record.ByteCount);
            BinaryPrimitives.WriteUInt32BigEndian(target[24..], record.FirstSeenUptime);
            BinaryPrimitives.WriteUInt32BigEndian(target[28..], record.LastSeenUptime);
            BinaryPrimitives.WriteUInt16BigEndian(target[32..], record.SourcePort);
            BinaryPrimitives.WriteUInt16BigEndian(target[34..], record.DestinationPort);
            target[38] = record.Protocol;
        }

        return datagram;
    }

    private static readonly TestRecord DefaultRecord = new(
        "192.168.88.10",
        "1.1.1.1",
        1,
        2,
        1,
        100,
        900,
        950,
        1234,
        443,
        6);

    private sealed record TestRecord(
        string SourceAddress,
        string DestinationAddress,
        ushort InputInterface,
        ushort OutputInterface,
        uint PacketCount,
        uint ByteCount,
        uint FirstSeenUptime,
        uint LastSeenUptime,
        ushort SourcePort,
        ushort DestinationPort,
        byte Protocol);
}
