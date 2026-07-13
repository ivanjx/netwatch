using System.Threading.Channels;

namespace NetWatch.Server.FlowCollection;

internal sealed class NetFlowChannel
{
    public const int DefaultCapacity = 50_000;

    private readonly Channel<NormalizedFlow> _channel = Channel.CreateBounded<NormalizedFlow>(
        new BoundedChannelOptions(DefaultCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

    public ChannelReader<NormalizedFlow> Reader => _channel.Reader;

    public ValueTask WriteAsync(NormalizedFlow flow, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(flow, cancellationToken);
}
