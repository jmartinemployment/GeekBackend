using System.Threading.Channels;

namespace GeekAPI.Services.GeekCrawler;

public sealed class GeekCrawlerWake
{
    private readonly Channel<Guid> _channel;

    public GeekCrawlerWake(GeekCrawlerOptions options)
    {
        _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
        {
            SingleReader = options.WorkerCount <= 1,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Wake(Guid runId) => _channel.Writer.TryWrite(runId);
}
