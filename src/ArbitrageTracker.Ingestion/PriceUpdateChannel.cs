using System.Threading.Channels;

namespace ArbitrageTracker.Ingestion;

/// <summary>Single-reader in-process channel coupling pollers to the detection pipeline.</summary>
public sealed class PriceUpdateChannel
{
    private readonly Channel<PriceUpdate> _channel =
        Channel.CreateBounded<PriceUpdate>(new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public ChannelWriter<PriceUpdate> Writer => _channel.Writer;
    public ChannelReader<PriceUpdate> Reader => _channel.Reader;
}
