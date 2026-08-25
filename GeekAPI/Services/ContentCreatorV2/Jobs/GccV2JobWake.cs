using System.Threading.Channels;

namespace GeekAPI.Services.ContentCreatorV2.Jobs;

/// <summary>
/// Single-process wake signal for <see cref="GccV2JobWorker"/>. No polling: producers
/// (controllers, the worker itself) call <see cref="Wake"/> after persisting a state change, and
/// the worker's <c>await foreach</c> over <see cref="Reader"/> blocks until a job id arrives.
/// </summary>
public sealed class GccV2JobWake
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Wake(Guid jobId) => _channel.Writer.TryWrite(jobId);
}
