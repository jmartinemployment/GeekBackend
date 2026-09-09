using System.Threading.Channels;

namespace GeekAPI.Services.ContentCreatorV2.AgentTests;

public sealed class GccV2AgentTestWake
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    public ChannelReader<Guid> Reader => _channel.Reader;
    public void Wake(Guid runId) => _channel.Writer.TryWrite(runId);
}
