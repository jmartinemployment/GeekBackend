using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Partner;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed record GccV2ProjectSiteKnowledgeResult(
    Guid AssetId, Guid VersionId, Guid ResourceId, int PageCount, string LifecycleState,
    string IngestionState, Guid? IngestionJobId);

public sealed class GccV2ProjectSiteKnowledgeService(
    HttpGccV2Repository repository,
    IGccV2ContextObjectStore objectStore,
    GccV2ContextIngestionWake ingestionWake,
    IConfiguration configuration)
{
    private const int MaximumNormalizedCharacters = 2_000_000;

    public async Task<GccV2ProjectSiteKnowledgeResult> PromoteAsync(
        string ownerUserId,
        Guid runId,
        string? name,
        IReadOnlyList<Guid>? selectedPageIds,
        bool approve,
        CancellationToken ct)
    {
        var run = await repository.GetProjectSiteCrawlRunAsync(runId, ct);
        if (run is null
            || !string.Equals(run.OwnerUserId, ownerUserId, StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException("Project-site crawl was not found.");
        if (!string.Equals(run.Status, "complete", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a completed project-site crawl can become an approved source.");

        var existing = (await repository.ListKnowledgeAsync(ownerUserId, ct))
            .SelectMany(asset => asset.Versions.Select(version => (asset, version)))
            .FirstOrDefault(item => SourceRunId(item.version.SourceDescriptorJson) == runId
                && item.version.LifecycleState is not ("revoked" or "deprecated")
                && item.version.Resources?.Any(resource =>
                    resource.ResourceKind is "original" or "normalized_text") == true);
        if (existing.version is not null)
        {
            var existingVersion = existing.version;
            var existingResource = existingVersion.Resources!
                .OrderByDescending(resource => resource.ResourceKind == "normalized_text")
                .First(resource => resource.ResourceKind is "original" or "normalized_text");
            if (approve && existingVersion.LifecycleState == "draft")
            {
                await repository.TransitionKnowledgeAsync(existingVersion.Id, "review",
                    new(ownerUserId, ownerUserId, "Promoted from completed owned website research."), ct);
                existingVersion = await repository.TransitionKnowledgeAsync(existingVersion.Id, "approve",
                    new(ownerUserId, ownerUserId, "Approved during website-source promotion."), ct);
            }
            else if (approve && existingVersion.LifecycleState == "in_review")
            {
                existingVersion = await repository.TransitionKnowledgeAsync(existingVersion.Id, "approve",
                    new(ownerUserId, ownerUserId, "Approved during website-source promotion."), ct);
            }

            Guid? ingestionJobId = null;
            var ingestionState = existingVersion.IndexState == "ready" ? "ready" : "processing";
            if (existingVersion.IndexState is not "ready")
            {
                var original = existingVersion.Resources!
                    .FirstOrDefault(resource => resource.ResourceKind == "original")
                    ?? existingResource;
                var requeued = await repository.QueueKnowledgeIngestionAsync(existingVersion.Id, new(
                    ownerUserId, original.ObjectKey, original.ByteSize, original.Sha256), ct);
                ingestionWake.Wake(requeued.Id);
                ingestionJobId = requeued.Id;
                ingestionState = string.IsNullOrWhiteSpace(requeued.Status) ? "processing" : requeued.Status;
            }

            return new GccV2ProjectSiteKnowledgeResult(
                existing.asset.Id, existingVersion.Id, existingResource.Id,
                SourcePageCount(existingVersion.SourceDescriptorJson),
                existingVersion.LifecycleState,
                ingestionState,
                ingestionJobId);
        }

        var selected = selectedPageIds is { Count: > 0 } ? selectedPageIds.ToHashSet() : null;
        var pages = await LoadPagesAsync(runId, ct);
        var content = BuildNormalizedContent(pages, selected, out var includedIds);
        if (includedIds.Count == 0)
            throw new InvalidOperationException("The selected crawl pages contain no usable text.");

        var bytes = Encoding.UTF8.GetBytes(content);
        var usage = await repository.GetContextQuotaUsageAsync(ownerUserId, ct: ct)
            ?? throw new InvalidOperationException("Source-library quota is unavailable.");
        var maximumOwnerBytes = configuration.GetValue<long?>(
            "ContentCreatorV2:ContextObjectStore:MaxOwnerBytes") ?? 1024L * 1024 * 1024;
        if (usage.KnowledgeBytes + usage.OwnerAttachmentBytes + bytes.LongLength > maximumOwnerBytes)
            throw new InvalidOperationException(
                $"Owner context quota of {maximumOwnerBytes} bytes would be exceeded.");
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var asset = await repository.CreateKnowledgeAsync(new(
            ownerUserId,
            string.IsNullOrWhiteSpace(name) ? $"Website: {new Uri(run.SiteUrl).Host}" : name.Trim(),
            "Reusable reference promoted from completed project website research.",
            JsonSerializer.Serialize(new[] { "project-site", new Uri(run.SiteUrl).Host }),
            "project_site_crawl",
            ownerUserId), ct);
        var canonical = GccV2CanonicalJson.Serialize(new
        {
            runId,
            pageIds = includedIds.Order().ToList(),
            contentSha256 = sha256,
            schemaVersion = 1,
        });
        var version = await repository.CreateKnowledgeVersionAsync(asset.Id, new(
            ownerUserId,
            1,
            GccV2CanonicalJson.Sha256(canonical),
            sha256,
            "text/plain",
            "en",
            JsonSerializer.Serialize(new
            {
                type = "project_site_crawl",
                runId,
                siteUrl = run.SiteUrl,
                pageIds = includedIds,
            }),
            JsonSerializer.Serialize(new
            {
                promotedBy = ownerUserId,
                promotedAtUtc = DateTimeOffset.UtcNow,
            }),
            run.CompletedAtUtc,
            ownerUserId), ct);

        var objectKey =
            $"content-creator-v2/{ownerUserId}/knowledge/{asset.Id}/{version.Id:N}/original.txt";
        await using (var stream = new MemoryStream(bytes, writable: false))
            await objectStore.PutAsync(objectKey, stream, "text/plain; charset=utf-8", ct);
        var resource = await repository.AddKnowledgeResourceAsync(version.Id, new(
            ownerUserId,
            "original",
            objectKey,
            bytes.LongLength,
            sha256,
            "text/plain; charset=utf-8",
            $"{new Uri(run.SiteUrl).Host}-website.txt",
            "quarantined",
            CoordinatesJson: JsonSerializer.Serialize(new { runId, pageIds = includedIds })), ct);

        var lifecycle = version.LifecycleState;
        if (approve)
        {
            await repository.TransitionKnowledgeAsync(version.Id, "review",
                new(ownerUserId, ownerUserId, "Promoted from completed owned website research."), ct);
            version = await repository.TransitionKnowledgeAsync(version.Id, "approve",
                new(ownerUserId, ownerUserId, "Approved during website-source promotion."), ct);
            lifecycle = version.LifecycleState;
        }

        var ingestion = await repository.QueueKnowledgeIngestionAsync(version.Id, new(
            ownerUserId, objectKey, bytes.LongLength, sha256), ct);
        ingestionWake.Wake(ingestion.Id);

        return new GccV2ProjectSiteKnowledgeResult(
            asset.Id, version.Id, resource.Id, includedIds.Count, lifecycle,
            ingestion.Status, ingestion.Id);
    }

    private async Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> LoadPagesAsync(
        Guid runId, CancellationToken ct)
    {
        var pages = new List<GccV2ProjectSiteCrawlPageDto>();
        for (var offset = 0; ; offset += 100)
        {
            var chunk = await repository.ListProjectSiteCrawlPagesAsync(runId, 100, offset, ct);
            if (chunk.Count == 0) break;
            pages.AddRange(chunk);
            if (chunk.Count < 100) break;
        }
        return pages;
    }

    public static string BuildNormalizedContent(
        IReadOnlyList<GccV2ProjectSiteCrawlPageDto> pages,
        IReadOnlySet<Guid>? selectedPageIds,
        out List<Guid> includedPageIds)
    {
        var output = new StringBuilder();
        includedPageIds = [];
        foreach (var page in pages.OrderBy(x => x.FinalUrl, StringComparer.Ordinal))
        {
            if (selectedPageIds is not null && !selectedPageIds.Contains(page.Id)) continue;
            if (!page.RobotsAllowed || page.StatusCode is < 200 or >= 400
                || string.IsNullOrWhiteSpace(page.Html)) continue;
            var url = string.IsNullOrWhiteSpace(page.FinalUrl) ? page.Url : page.FinalUrl;
            var extracted = GccV2ArticleHtmlExtractor.ExtractPartnerPage(url, page.Html);
            if (extracted.Paragraphs.Count == 0) continue;
            var pageText = new StringBuilder()
                .AppendLine($"Source: {url}")
                .AppendLine($"Title: {extracted.Title}");
            foreach (var heading in extracted.Headings)
                pageText.AppendLine($"{new string('#', Math.Clamp(heading.Level, 1, 6))} {heading.Text}");
            foreach (var paragraph in extracted.Paragraphs)
                pageText.AppendLine(paragraph);
            pageText.AppendLine();
            if (output.Length + pageText.Length > MaximumNormalizedCharacters) break;
            output.Append(pageText);
            includedPageIds.Add(page.Id);
        }
        return output.ToString();
    }

    private static Guid? SourceRunId(string descriptorJson)
    {
        try
        {
            using var document = JsonDocument.Parse(descriptorJson);
            return document.RootElement.TryGetProperty("runId", out var value)
                && Guid.TryParse(value.GetString(), out var runId)
                    ? runId
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int SourcePageCount(string descriptorJson)
    {
        try
        {
            using var document = JsonDocument.Parse(descriptorJson);
            return document.RootElement.TryGetProperty("pageIds", out var value)
                && value.ValueKind == JsonValueKind.Array
                    ? value.GetArrayLength()
                    : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
