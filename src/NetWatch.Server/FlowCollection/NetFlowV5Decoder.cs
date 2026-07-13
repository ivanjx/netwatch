using System.Buffers.Binary;
using System.Net;

namespace NetWatch.Server.FlowCollection;

internal static class NetFlowV5Decoder
{
    private const int HeaderLength = 24;
    private const int RecordLength = 48;

    public static bool TryDecode(
        ReadOnlySpan<byte> datagram,
        out NetFlowV5Datagram? decoded,
        out string? error)
    {
        decoded = null;
        error = null;

        if (datagram.Length < HeaderLength)
        {
            error = $"Datagram is shorter than the {HeaderLength}-byte NetFlow v5 header.";
            return false;
        }

        var version = BinaryPrimitives.ReadUInt16BigEndian(datagram);
        if (version != 5)
        {
            error = $"Unsupported NetFlow version {version}.";
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(datagram[2..]);
        var expectedLength = HeaderLength + (count * RecordLength);
        if (datagram.Length != expectedLength)
        {
            error = $"Header declares {count} records ({expectedLength} bytes), but datagram has {datagram.Length} bytes.";
            return false;
        }

        var records = new NetFlowV5Record[count];
        for (var index = 0; index < count; index++)
        {
            var record = datagram.Slice(HeaderLength + (index * RecordLength), RecordLength);
            records[index] = new NetFlowV5Record(
                ReadAddress(record),
                ReadAddress(record[4..]),
                BinaryPrimitives.ReadUInt16BigEndian(record[12..]),
                BinaryPrimitives.ReadUInt16BigEndian(record[14..]),
                BinaryPrimitives.ReadUInt32BigEndian(record[16..]),
                BinaryPrimitives.ReadUInt32BigEndian(record[20..]),
                BinaryPrimitives.ReadUInt32BigEndian(record[24..]),
                BinaryPrimitives.ReadUInt32BigEndian(record[28..]),
                BinaryPrimitives.ReadUInt16BigEndian(record[32..]),
                BinaryPrimitives.ReadUInt16BigEndian(record[34..]),
                record[38]);
        }

        decoded = new NetFlowV5Datagram(
            BinaryPrimitives.ReadUInt32BigEndian(datagram[4..]),
            BinaryPrimitives.ReadUInt32BigEndian(datagram[8..]),
            BinaryPrimitives.ReadUInt32BigEndian(datagram[12..]),
            BinaryPrimitives.ReadUInt32BigEndian(datagram[16..]),
            records);
        return true;
    }

    private static IPAddress ReadAddress(ReadOnlySpan<byte> value) => new(value[..4]);
}

internal sealed record NetFlowV5Datagram(
    uint SystemUptimeMilliseconds,
    uint UnixSeconds,
    uint UnixNanoseconds,
    uint FlowSequence,
    IReadOnlyList<NetFlowV5Record> Records);

internal sealed record NetFlowV5Record(
    IPAddress SourceAddress,
    IPAddress DestinationAddress,
    ushort InputInterface,
    ushort OutputInterface,
    uint PacketCount,
    uint ByteCount,
    uint FirstSeenUptimeMilliseconds,
    uint LastSeenUptimeMilliseconds,
    ushort SourcePort,
    ushort DestinationPort,
    byte Protocol);
