using System.Buffers.Binary;

using NetWatch.Server.FlowCollection;

namespace NetWatch.Server.Tests.FlowCollection;

public sealed class NetFlowV5DecoderTests
{
    [Fact]
    public void TryDecode_DecodesRepresentativeRecord()
    {
        var datagram = new byte[72];
        BinaryPrimitives.WriteUInt16BigEndian(datagram, 5);
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(4), 120_000);
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(8), 1_700_000_000);
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(16), 42);

        var record = datagram.AsSpan(24);
        new byte[] { 192, 168, 20, 25 }.CopyTo(record);
        new byte[] { 1, 1, 1, 1 }.CopyTo(record[4..]);
        BinaryPrimitives.WriteUInt16BigEndian(record[12..], 3);
        BinaryPrimitives.WriteUInt16BigEndian(record[14..], 7);
        BinaryPrimitives.WriteUInt32BigEndian(record[16..], 10);
        BinaryPrimitives.WriteUInt32BigEndian(record[20..], 1_500);
        BinaryPrimitives.WriteUInt16BigEndian(record[32..], 51_234);
        BinaryPrimitives.WriteUInt16BigEndian(record[34..], 443);
        record[38] = 6;

        var success = NetFlowV5Decoder.TryDecode(datagram, out var decoded, out var error);

        Assert.True(success, error);
        var flow = Assert.Single(decoded!.Records);
        Assert.Equal("192.168.20.25", flow.SourceAddress.ToString());
        Assert.Equal("1.1.1.1", flow.DestinationAddress.ToString());
        Assert.Equal((uint)1_500, flow.ByteCount);
        Assert.Equal((ushort)443, flow.DestinationPort);
        Assert.Equal((byte)6, flow.Protocol);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(71)]
    [InlineData(73)]
    public void TryDecode_RejectsInvalidLengths(int length)
    {
        var datagram = new byte[length];
        if (length >= 4)
        {
            BinaryPrimitives.WriteUInt16BigEndian(datagram, 5);
            BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(2), 1);
        }

        Assert.False(NetFlowV5Decoder.TryDecode(datagram, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TryDecode_RejectsUnsupportedVersion()
    {
        var datagram = new byte[24];
        BinaryPrimitives.WriteUInt16BigEndian(datagram, 9);

        Assert.False(NetFlowV5Decoder.TryDecode(datagram, out _, out var error));
        Assert.Contains("version 9", error);
    }
}
