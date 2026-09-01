using System.Threading.Channels;

namespace GeekAPI.Services.ContentCreatorV2.ProjectSite;

public sealed class GccV2ProjectSiteCrawlWake
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Wake(Guid runId)
    {
        if (runId == Guid.Empty) return;
        _channel.Writer.TryWrite(runId);
    }
}
