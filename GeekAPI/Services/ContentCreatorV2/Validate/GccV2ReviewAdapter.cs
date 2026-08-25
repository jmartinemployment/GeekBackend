using System.Text.Json;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Services.Review;

namespace GeekAPI.Services.ContentCreatorV2.Validate;

/// <summary>Normalized result of one editorial review pass, in v2's own shape.</summary>
public sealed record GccV2ReviewResult(string Verdict, string? Notes);

/// <summary>
/// Builds an in-memory, <b>non-persisted</b> <see cref="GeneratedContent"/> + minimal context and
/// calls the shared <see cref="IEditorialReviewService"/> — zero edits to that service. This is the
/// seam the design plan calls out: <c>EditorialReviewService.ReviewAsync</c> is typed for the old
/// Project domain, so v2 adapts to it here instead of copying/forking the reviewer.
/// </summary>
public sealed class GccV2ReviewAdapter
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = true };

    private readonly IEditorialReviewService _review;

    public GccV2ReviewAdapter(IEditorialReviewService review) => _review = review;

    public async Task<GccV2ReviewResult> ReviewAsync(
        string contentType,
        string title,
        string? metaDescription,
        ContentDocument body,
        ProjectGenerationContext context,
        LlmProviderType generatedByProvider,
        CancellationToken ct)
    {
        var content = new GeneratedContent
        {
            ContentType = ToGeneratedContentType(contentType),
            Title = title,
            Slug = string.Empty,
            Body = body,
            MetaDescription = metaDescription,
            GeneratedByProvider = generatedByProvider,
            GeneratedByModel = string.Empty,
        };

        var outcome = await _review.ReviewAsync(content, context, ct);
        var verdict = outcome.Status switch
        {
            ReviewVerdictStatus.Approved => "approved",
            _ => "revise",
        };

        string? notes = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<ReviewJsonShape>(outcome.NotesJson, JsonOpts);
            notes = parsed?.Notes;
        }
        catch (JsonException)
        {
            // The reviewer's raw response didn't parse as the expected {"verdict","notes"} shape —
            // surface it verbatim rather than silently dropping the reviewer's feedback.
            notes = outcome.NotesJson;
        }

        return new GccV2ReviewResult(verdict, notes);
    }

    private static GeneratedContentType ToGeneratedContentType(string contentType) => contentType.ToLowerInvariant() switch
    {
        "pillar" or "tool" => GeneratedContentType.TechnicalArticle,
        "blog" => GeneratedContentType.BlogPost,
        "email" => GeneratedContentType.EmailColdOutreach,
        "social" => GeneratedContentType.SocialLinkedIn,
        "ads" => GeneratedContentType.Advertising,
        _ => GeneratedContentType.BlogPost,
    };

    private sealed record ReviewJsonShape(string? Verdict, string? Notes);
}
