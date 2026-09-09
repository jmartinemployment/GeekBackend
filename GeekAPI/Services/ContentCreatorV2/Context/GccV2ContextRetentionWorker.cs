using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed class GccV2ContextRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<GccV2ContextRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SweepAsync(stoppingToken);
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
            var objectStore = scope.ServiceProvider.GetRequiredService<IGccV2ContextObjectStore>();
            var indexer = scope.ServiceProvider.GetRequiredService<IGccV2KnowledgeIndexer>();
            foreach (var attachment in await repository.ListRunAttachmentsDueForRetentionAsync(ct: ct))
            {
                try
                {
                    await indexer.DeleteAsync(
                        new(attachment.OwnerUserId, attachment.Id, attachment.Id), ct);
                    await objectStore.DeleteAsync(attachment.ObjectKey, ct);
                    await repository.DeleteRunAttachmentAsync(
                        attachment.Id, attachment.OwnerUserId, ct);
                    GccV2ContextMetrics.RetentionOutcomes.Add(
                        1, new KeyValuePair<string, object?>("outcome", "deleted"));
                }
                catch (Exception ex)
                {
                    GccV2ContextMetrics.RetentionOutcomes.Add(
                        1, new KeyValuePair<string, object?>("outcome", "failed"));
                    logger.LogWarning(ex,
                        "Context retention failed for attachment {AttachmentId}.", attachment.Id);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Context retention sweep failed.");
        }
    }
}
