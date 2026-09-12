using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GeekAPI.HttpClients;
using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed class GccV2ContextIngestionWake
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    public ChannelReader<Guid> Reader => _channel.Reader;
    public void Wake(Guid id) => _channel.Writer.TryWrite(id);
}

public interface IGccV2KnowledgeIndexer
{
    Task IndexAsync(GccV2KnowledgeIndexRequest request, CancellationToken ct);
    Task DeleteAsync(GccV2KnowledgeDeleteRequest request, CancellationToken ct);
}

public sealed record GccV2KnowledgeIndexRequest(
    string OwnerUserId, Guid AssetId, Guid AssetVersionId, Guid ResourceId,
    string SourceSha256, string DerivedSha256, string ObjectKey, string MediaType,
    string ParserName, string ParserVersion, string Content, string Lifecycle,
    JsonElement SourceCoordinates, string ContextKind = "knowledge");
public sealed record GccV2KnowledgeDeleteRequest(
    string OwnerUserId, Guid AssetVersionId, Guid ResourceId);

public sealed class GccV2HttpKnowledgeIndexer(HttpClient http) : IGccV2KnowledgeIndexer
{
    public async Task IndexAsync(GccV2KnowledgeIndexRequest request, CancellationToken ct)
    {
        if (http.BaseAddress is null)
            throw new InvalidOperationException("GEEK_CRAWLER_RAG_URL is required for Knowledge indexing.");
        using var response = await http.PostAsJsonAsync("v1/context/assets/index", request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Knowledge index rejected the revision with HTTP {(int)response.StatusCode}.");
    }

    public async Task DeleteAsync(GccV2KnowledgeDeleteRequest request, CancellationToken ct)
    {
        if (http.BaseAddress is null)
            throw new InvalidOperationException("GEEK_CRAWLER_RAG_URL is required for Knowledge deletion.");
        using var response = await http.PostAsJsonAsync("v1/context/assets/delete", request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Knowledge index rejected the tombstone with HTTP {(int)response.StatusCode}.");
    }
}

public sealed class GccV2ContextIngestionNotifier(IHubContext<GccV2RealtimeHub> hub)
{
    public async Task NotifyAsync(GccV2ContextIngestionJobDto job, CancellationToken ct)
    {
        var payload = ToEvent(job);
        await hub.Clients.Group(GccV2RealtimeHub.ContextIngestionGroup(job.Id))
            .SendAsync("ContextIngestionEvent", payload, ct);
        await hub.Clients.Group(GccV2RealtimeHub.ContextIngestionOwnerGroup(job.OwnerUserId))
            .SendAsync("ContextIngestionEvent", payload, ct);
    }

    public static object ToEvent(GccV2ContextIngestionJobDto job)
    {
        var latest = job.Events?.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
        return new
        {
            id = latest?.Id ?? job.Id,
            jobId = job.Id,
            seq = (latest?.CreatedAtUtc ?? job.UpdatedAtUtc).ToUnixTimeMilliseconds(),
            state = job.Status,
            message = latest?.Type ?? $"Context ingestion {job.Status}",
            progressPercent = job.ProgressPercent,
            createdAtUtc = latest?.CreatedAtUtc ?? job.UpdatedAtUtc,
            assetId = job.TargetKind == "knowledge" ? job.TargetId : (Guid?)null,
        };
    }
}

public sealed class GccV2ContextIngestionWorker(
    GccV2ContextIngestionWake wake,
    IServiceScopeFactory scopeFactory,
    ILogger<GccV2ContextIngestionWorker> logger) : BackgroundService
{
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverOnce(stoppingToken);
        while (await wake.Reader.WaitToReadAsync(stoppingToken))
        {
            while (wake.Reader.TryRead(out var id))
            {
                try { await ProcessAsync(id, stoppingToken); }
                catch (Exception ex) { logger.LogError(ex, "Context ingestion wake failed for {JobId}.", id); }
            }
        }
    }

    private async Task RecoverOnce(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        foreach (var job in await repo.ListContextIngestionJobsAsync("queued", limit: 200, ct: ct))
            wake.Wake(job.Id);
        foreach (var job in await repo.ListContextIngestionJobsAsync("running", DateTimeOffset.UtcNow, 200, ct))
            wake.Wake(job.Id);
    }

    private async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        var store = scope.ServiceProvider.GetRequiredService<IGccV2ContextObjectStore>();
        var scanner = scope.ServiceProvider.GetRequiredService<IGccV2MalwareScanner>();
        var extractor = scope.ServiceProvider.GetRequiredService<GccV2DocumentExtractor>();
        var indexer = scope.ServiceProvider.GetRequiredService<IGccV2KnowledgeIndexer>();
        var notifier = scope.ServiceProvider.GetRequiredService<GccV2ContextIngestionNotifier>();
        var job = await repo.ClaimContextIngestionJobAsync(id, _instanceId, 300, ct);
        if (job is null) return;
        try
        {
            await Transition(repo, notifier, id, "scanning", 10, ct);
            if (job.TargetKind == "run_attachment")
            {
                var attachment = await repo.GetRunAttachmentAsync(job.TargetId, job.OwnerUserId, ct)
                    ?? throw new InvalidOperationException("Run attachment disappeared.");
                await using (var scan = await store.OpenReadAsync(attachment.ObjectKey, ct))
                    await scanner.ScanAsync(scan, ct);
                await using var source = await store.OpenReadAsync(attachment.ObjectKey, ct);
                var extractedAttachment = await extractor.ExtractAsync(
                    source, attachment.MediaType, attachment.ByteSize, ct);
                var attachmentDerivedSha = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(extractedAttachment.Text))).ToLowerInvariant();
                await indexer.IndexAsync(new(
                    job.OwnerUserId, attachment.Id, attachment.Id, attachment.Id,
                    attachment.Sha256, attachmentDerivedSha, attachment.ObjectKey, "text/plain",
                    extractedAttachment.ParserName, extractedAttachment.ParserVersion,
                    extractedAttachment.Text, "approved",
                    JsonSerializer.Deserialize<JsonElement>(extractedAttachment.CoordinatesJson),
                    "run_attachment"), ct);
                var readyAttachment = await repo.TransitionContextIngestionJobAsync(id, new(
                    "ready", 100, "ready", JsonSerializer.Serialize(new { attachmentId = attachment.Id }),
                    IndexState: "not_applicable"), ct);
                await notifier.NotifyAsync(readyAttachment, ct);
                RecordOutcome(job, "succeeded");
                return;
            }

            var lookup = await repo.GetContextVersionAsync("knowledge", job.TargetId, job.OwnerUserId, ct)
                ?? throw new InvalidOperationException("Knowledge revision disappeared.");
            var asset = await repo.GetKnowledgeAsync(lookup.StableId, job.OwnerUserId, ct)
                ?? throw new InvalidOperationException("Knowledge asset disappeared.");
            var version = asset.Versions.Single(x => x.Id == job.TargetId);
            var original = version.Resources?.Single(x => x.ResourceKind == "original")
                ?? throw new InvalidOperationException("Original resource disappeared.");
            await using (var scan = await store.OpenReadAsync(original.ObjectKey, ct))
                await scanner.ScanAsync(scan, ct);
            await Transition(repo, notifier, id, "extracting", 35, ct);
            await using var input = await store.OpenReadAsync(original.ObjectKey, ct);
            var extracted = await extractor.ExtractAsync(input, original.MediaType, original.ByteSize, ct);
            var derivedBytes = Encoding.UTF8.GetBytes(extracted.Text);
            var derivedSha = Convert.ToHexString(SHA256.HashData(derivedBytes)).ToLowerInvariant();
            var derivedKey = original.ObjectKey + ".normalized.txt";
            await using (var derivedStream = new MemoryStream(derivedBytes, writable: false))
                await store.PutAsync(derivedKey, derivedStream, "text/plain; charset=utf-8", ct);
            var resource = await repo.AddKnowledgeResourceAsync(version.Id, new(
                job.OwnerUserId, "normalized_text", derivedKey, derivedBytes.Length, derivedSha,
                "text/plain", original.SafeFileName + ".txt", "clean",
                extracted.ParserName, extracted.ParserVersion, derivedSha,
                extracted.CoordinatesJson), ct);
            await Transition(repo, notifier, id, "indexing", 75, ct);
            await indexer.IndexAsync(new(job.OwnerUserId, asset.Id, version.Id, resource.Id,
                original.Sha256, derivedSha, derivedKey, "text/plain",
                extracted.ParserName, extracted.ParserVersion, extracted.Text,
                version.LifecycleState,
                JsonSerializer.Deserialize<JsonElement>(extracted.CoordinatesJson)), ct);
            var ready = await repo.TransitionContextIngestionJobAsync(id, new(
                "ready", 100, "ready", JsonSerializer.Serialize(new
                {
                    assetId = asset.Id, versionId = version.Id, resourceId = resource.Id,
                    sourceSha256 = original.Sha256, derivedSha256 = derivedSha,
                }), IndexState: "ready"), ct);
            await notifier.NotifyAsync(ready, ct);
            await ApproveIfRequestedAsync(repo, job.OwnerUserId, version, ct);
            RecordOutcome(job, "succeeded");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Context ingestion {JobId} failed closed.", id);
            RecordOutcome(job, "failed");
            try
            {
                var failed = await repo.TransitionContextIngestionJobAsync(id, new(
                    "failed", 100, "failed", "{}", Sanitize(ex.Message), "failed"), ct);
                await notifier.NotifyAsync(failed, ct);
            }
            catch (Exception transitionEx)
            {
                logger.LogError(transitionEx, "Could not persist terminal failure for context ingestion {JobId}.", id);
            }
        }
    }

    /// <summary>
    /// Finishes an approval the promoting caller asked for but could not perform: approval is
    /// legal only once extraction and indexing are ready, which this job has just made true.
    /// </summary>
    private async Task ApproveIfRequestedAsync(
        HttpGccV2Repository repo, string ownerUserId, GccV2KnowledgeAssetVersionDto version,
        CancellationToken ct)
    {
        if (!GccV2ProjectSiteKnowledgeService.WantsAutoApproval(version.ProvenanceJson)) return;
        if (version.LifecycleState is not ("draft" or "in_review")) return;
        try
        {
            var current = version.LifecycleState == "draft"
                ? await repo.TransitionKnowledgeAsync(version.Id, "review",
                    new(ownerUserId, ownerUserId, "Indexed after website-source promotion."), ct)
                : version;
            if (current.LifecycleState == "in_review")
                await repo.TransitionKnowledgeAsync(version.Id, "approve",
                    new(ownerUserId, ownerUserId, "Approved after website-source promotion indexed."), ct);
        }
        catch (Exception ex)
        {
            // Ingestion itself succeeded; leave the revision reviewable rather than failing the job.
            logger.LogWarning(ex, "Auto-approval after ingestion failed for knowledge {VersionId}.", version.Id);
        }
    }

    private static async Task Transition(
        HttpGccV2Repository repo, GccV2ContextIngestionNotifier notifier,
        Guid id, string state, int progress, CancellationToken ct)
    {
        var job = await repo.TransitionContextIngestionJobAsync(id,
            new(state, progress, state, JsonSerializer.Serialize(new { status = state, progress })), ct);
        await notifier.NotifyAsync(job, ct);
    }

    private static string Sanitize(string value) =>
        value.Length <= 1000 ? value : value[..1000];

    private static void RecordOutcome(GccV2ContextIngestionJobDto job, string outcome)
    {
        GccV2ContextMetrics.IngestionOutcomes.Add(
            1, new KeyValuePair<string, object?>("outcome", outcome));
        GccV2ContextMetrics.IngestionLatency.Record(
            Math.Max(0, (DateTimeOffset.UtcNow - job.CreatedAtUtc).TotalSeconds));
    }
}

public sealed class GccV2ContextIngestionListenService(
    GccV2ContextIngestionWake wake,
    ILogger<GccV2ContextIngestionListenService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var value = Environment.GetEnvironmentVariable("GCC_V2_LISTEN_DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(value)) return;
        var connectionString = Normalize(value);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                connection.Notification += (_, e) =>
                {
                    if (Guid.TryParse(e.Payload, out var id)) wake.Wake(id);
                };
                await using var command = new NpgsqlCommand("LISTEN gcc_v2_context_ingestion;", connection);
                await command.ExecuteNonQueryAsync(stoppingToken);
                while (!stoppingToken.IsCancellationRequested) await connection.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Context ingestion LISTEN failed; reconnecting.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private static string Normalize(string raw)
    {
        if (!raw.Contains("://", StringComparison.Ordinal)) return raw.Trim();
        var uri = new Uri(raw.Trim());
        var user = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host, Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : "",
            Database = uri.AbsolutePath.Trim('/'),
            SslMode = Npgsql.SslMode.Prefer,
        }.ConnectionString;
    }
}
