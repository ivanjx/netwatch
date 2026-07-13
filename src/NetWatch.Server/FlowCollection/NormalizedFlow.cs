using System.Net;

namespace NetWatch.Server.FlowCollection;

internal sealed record NormalizedFlow(
    IPAddress ExporterAddress,
    DateTimeOffset ExportedAtUtc,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    IPAddress SourceAddress,
    IPAddress DestinationAddress,
    ushort InputInterface,
    ushort OutputInterface,
    uint PacketCount,
    uint ByteCount,
    ushort SourcePort,
    ushort DestinationPort,
    byte Protocol);
