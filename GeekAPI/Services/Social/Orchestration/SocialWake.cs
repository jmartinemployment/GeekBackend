using System.Threading.Channels;

namespace GeekAPI.Services.Social.Orchestration;

/// <summary>
/// How the Listener wakes the orchestrator: the id of an event the store just accepted. Nothing is
/// held here that the store does not also hold, so a wake lost with a restart costs nothing; the
/// orchestrator finds every event still untriaged when it starts.
/// </summary>
public sealed class SocialWake
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Wake(Guid eventId) => _channel.Writer.TryWrite(eventId);
}
