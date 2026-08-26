using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Transforms;
using GeekAPI.Services.Workflow.Providers;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

[ApiController]
[Route("api/geek-content-creator-v2/creates/{createId:guid}/transform")]
public class GccV2TransformController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly ICurrentUserContext _user;
    private readonly HttpGccV2Repository _repo;
    private readonly GccV2RepurposeTransformService _transform;
    private readonly IContentProviderFactory _providers;

    public GccV2TransformController(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2RepurposeTransformService transform,
        IContentProviderFactory providers)
    {
        _user = user;
        _repo = repo;
        _transform = transform;
        _providers = providers;
    }

    /// <summary>
    /// Sync Re-Purpose: ready draft → channel variants. Image prompts: one per H2 for pillar/blog
    /// sources; one for all other content types.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<object>> Transform(Guid createId, [FromBody] TransformRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null) return NotFound();
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        GccV2JobDto? job = null;
        if (request?.JobId is { } jobId && jobId != Guid.Empty)
        {
            job = await _repo.GetJobAsync(jobId, ct);
            if (job is null || job.CreateId != createId || !IsOwner(job.OwnerUserId))
                return NotFound();
        }
        else
        {
            job = await _repo.GetLatestJobByCreateAsync(createId, ct);
        }

        if (job is null || string.IsNullOrWhiteSpace(job.ResultJson))
            return BadRequest(new { error = "No completed job result to re-purpose — generate content first." });

        var sourceJson = ExtractSourceDocumentJson(job.ResultJson);
        if (string.IsNullOrWhiteSpace(sourceJson))
            return BadRequest(new { error = "Job result has no document body to re-purpose." });

        var imagePromptCount = ResolveImagePromptCount(job.ContentType, job.ResultJson);
        var countOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["image_prompt"] = imagePromptCount,
        };

        try
        {
            var provider = _providers.GetDefault();
            var result = await _transform.ApplyAsync(sourceJson, request?.Channels, provider, ct, countOverrides);
            return Ok(new
            {
                createId,
                jobId = job.Id,
                imagePromptCount,
                variants = result.Variants.Select(v => new
                {
                    v.Channel,
                    v.Title,
                    v.Headline,
                    v.Body,
                    v.Cta,
                    v.Hashtags,
                    v.ContentDocumentJson,
                }),
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Pillar/Blog: one image prompt per top-level H2 section (minimum 1).
    /// All other content types: exactly one.
    /// </summary>
    internal static int ResolveImagePromptCount(string? contentType, string resultJson)
    {
        var type = (contentType ?? "").Trim().ToLowerInvariant();
        if (type is "pillar" or "blog")
        {
            var h2 = CountTopLevelH2Sections(resultJson);
            return Math.Max(1, h2);
        }

        return 1;
    }

    private static int CountTopLevelH2Sections(string resultJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            if (!TryGetDocument(root, out var document))
                return 0;

            if (!document.TryGetProperty("sections", out var sections)
                && !document.TryGetProperty("Sections", out sections))
                return 0;

            if (sections.ValueKind != JsonValueKind.Array)
                return 0;

            var count = 0;
            foreach (var section in sections.EnumerateArray())
            {
                var tag = ReadString(section, "tag") ?? ReadString(section, "Tag") ?? "";
                if (tag.Equals("h2", StringComparison.OrdinalIgnoreCase))
                    count++;
            }

            return count;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static bool TryGetDocument(JsonElement root, out JsonElement document)
    {
        if (root.TryGetProperty("document", out document) || root.TryGetProperty("Document", out document))
            return true;
        document = root;
        return root.TryGetProperty("sections", out _) || root.TryGetProperty("Sections", out _);
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static string ExtractSourceDocumentJson(string resultJson)
    {
        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("document", out var document))
            return JsonSerializer.Serialize(document, JsonOpts);

        return resultJson;
    }

    private bool IsOwner(string ownerUserId) =>
        _user.UserId != Guid.Empty && string.Equals(ownerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase);
}

public sealed record TransformRequest(IReadOnlyList<string>? Channels, Guid? JobId = null);
