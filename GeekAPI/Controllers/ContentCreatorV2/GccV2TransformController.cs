using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Transforms;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
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

    /// <summary>Sync repurpose: canonical draft → channel variants (LinkedIn, X, email, ads, …).</summary>
    [HttpPost]
    public async Task<ActionResult<object>> Transform(Guid createId, [FromBody] TransformRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null) return NotFound();
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        var job = await _repo.GetLatestJobByCreateAsync(createId, ct);
        if (job is null || string.IsNullOrWhiteSpace(job.ResultJson))
            return BadRequest(new { error = "No completed job result to transform — generate content first." });

        var sourceJson = ExtractSourceDocumentJson(job.ResultJson);
        if (string.IsNullOrWhiteSpace(sourceJson))
            return BadRequest(new { error = "Job result has no document body to transform." });

        try
        {
            var provider = _providers.GetDefault();
            var result = await _transform.ApplyAsync(sourceJson, request?.Channels, provider, ct);
            return Ok(new
            {
                createId,
                jobId = job.Id,
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

public sealed record TransformRequest(IReadOnlyList<string>? Channels);
