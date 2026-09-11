using System.Net;
using System.Security.Cryptography;
using System.Text;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed record GccV2UrlAttachmentResult(
    Guid AttachmentId,
    Guid CreateId,
    string FinalUrl,
    string Title,
    string SafeFileName,
    string ContentCompleteness,
    int StatusCode,
    long ByteSize,
    string ContentSha256,
    string IngestionState,
    Guid IngestionJobId);

/// <summary>
/// Run-scoped URL → temporary attachment (30-day retention). Reuses SSRF-gated page hydrate.
/// </summary>
public sealed class GccV2UrlAttachmentService(
    HttpGccV2Repository repository,
    IGccV2ContextObjectStore objectStore,
    GccV2ContextIngestionWake ingestionWake,
    GccV2TaskAgentPageHydrator pageHydrator,
    IConfiguration configuration)
{
    public async Task<(GccV2UrlAttachmentResult? Result, HttpStatusCode Status, string? Error, string? ErrorCode)>
        IngestAsync(string ownerUserId, Guid createId, string? url, string? name, CancellationToken ct)
    {
        var create = await repository.GetCreateAsync(createId, ct).ConfigureAwait(false);
        if (create is null
            || !string.Equals(create.OwnerUserId, ownerUserId, StringComparison.OrdinalIgnoreCase))
        {
            return (null, HttpStatusCode.NotFound, "Create was not found.", "not_found");
        }

        var outcome = await pageHydrator.HydrateAsync(url, ct).ConfigureAwait(false);
        if (!outcome.Ok || string.IsNullOrWhiteSpace(outcome.VisibleContent))
        {
            return (null, outcome.HttpStatus, outcome.ErrorMessage ?? "Could not fetch page content.",
                outcome.ErrorCode ?? "extract");
        }

        var bytes = Encoding.UTF8.GetBytes(outcome.VisibleContent);
        var usage = await repository.GetContextQuotaUsageAsync(ownerUserId, createId, ct)
            .ConfigureAwait(false);
        if (usage is null)
            return (null, HttpStatusCode.ServiceUnavailable, "Source-library quota is unavailable.", "quota");

        var maximumOwnerBytes = configuration.GetValue<long?>(
            "ContentCreatorV2:ContextObjectStore:MaxOwnerBytes") ?? 1024L * 1024 * 1024;
        var maximumRunBytes = configuration.GetValue<long?>(
            "ContentCreatorV2:ContextObjectStore:MaxRunBytes") ?? 100L * 1024 * 1024;
        if (usage.KnowledgeBytes + usage.OwnerAttachmentBytes + bytes.LongLength > maximumOwnerBytes)
        {
            return (null, HttpStatusCode.RequestEntityTooLarge,
                $"Owner context quota of {maximumOwnerBytes} bytes would be exceeded.", "quota");
        }

        if (usage.RunAttachmentBytes + bytes.LongLength > maximumRunBytes)
        {
            return (null, HttpStatusCode.RequestEntityTooLarge,
                $"Run attachment quota of {maximumRunBytes} bytes would be exceeded.", "quota");
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var finalUrl = outcome.FinalUrl ?? url!.Trim();
        var host = Uri.TryCreate(finalUrl, UriKind.Absolute, out var finalUri)
            ? finalUri.Host
            : "url";
        var title = string.IsNullOrWhiteSpace(outcome.Title) ? host : outcome.Title!.Trim();
        var safeName = string.IsNullOrWhiteSpace(name)
            ? $"{SanitizeFileName(host)}.md"
            : $"{SanitizeFileName(name)}.md";
        var objectKey =
            $"content-creator-v2/{ownerUserId}/attachments/{createId}/{Guid.NewGuid():N}/original.md";

        await using (var stream = new MemoryStream(bytes, writable: false))
        {
            await objectStore.PutAsync(objectKey, stream, "text/markdown; charset=utf-8", ct)
                .ConfigureAwait(false);
        }

        var verified = await objectStore.VerifyAsync(objectKey, bytes.LongLength, ct)
            .ConfigureAwait(false);
        if (verified.ByteSize != bytes.LongLength || verified.Sha256 != sha256)
        {
            return (null, HttpStatusCode.Conflict,
                "Stored object size or checksum does not match the fetched page.", "object");
        }

        var now = DateTimeOffset.UtcNow;
        var attachment = await repository.CreateRunAttachmentAsync(new(
            ownerUserId,
            createId,
            objectKey,
            safeName,
            "text/markdown",
            verified.ByteSize,
            verified.Sha256,
            now.AddMinutes(10),
            now.AddDays(30)), ct).ConfigureAwait(false);

        var finalized = await repository.FinalizeRunAttachmentAsync(
            attachment.Id, new(ownerUserId, verified.ByteSize, verified.Sha256), ct)
            .ConfigureAwait(false);

        var jobs = await repository.ListContextIngestionJobsAsync("queued", limit: 200, ct: ct)
            .ConfigureAwait(false);
        var job = jobs.SingleOrDefault(x => x.TargetKind == "run_attachment" && x.TargetId == attachment.Id);
        if (job is null)
        {
            return (null, HttpStatusCode.Conflict, "Attachment ingestion job was not queued.", "ingestion");
        }

        ingestionWake.Wake(job.Id);

        return (new GccV2UrlAttachmentResult(
            finalized.Id,
            createId,
            finalUrl,
            title,
            safeName,
            outcome.ContentCompleteness ?? "partial",
            outcome.StatusCode ?? 0,
            verified.ByteSize,
            verified.Sha256,
            string.IsNullOrWhiteSpace(job.Status) ? "queued" : job.Status,
            job.Id), HttpStatusCode.Accepted, null, null);
    }

    private static string SanitizeFileName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '.' ? ch : '-').ToArray();
        var cleaned = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "page" : cleaned;
    }
}
