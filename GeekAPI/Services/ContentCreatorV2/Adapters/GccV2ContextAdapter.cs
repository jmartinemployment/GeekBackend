using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Services;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentCreatorV2.Adapters;

/// <summary>
/// Translates a v2 <see cref="GccV2BriefDto"/> (+ optional <see cref="GccV2BrandKitContent"/>) into
/// the shared Workflow <see cref="ProjectGenerationContext"/> that <c>IContentPromptBuilder</c> /
/// <c>IEditorialReviewService</c> expect. Shape copied from
/// <c>GccGenerateService.BuildMinimalContext</c> (~line 1228) into this new v2-only file — v1 is
/// never edited or referenced here.
/// </summary>
public sealed class GccV2ContextAdapter
{
    private static readonly JsonSerializerOptions BriefJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccV2ContextAdapter> _logger;

    public GccV2ContextAdapter(IOptions<CompanyProfileOptions> company, ILogger<GccV2ContextAdapter> logger)
    {
        _company = company.Value;
        _logger = logger;
    }

    /// <summary>Builds the base per-job context shared by every section call. Per-section
    /// assignment (job/hierarchy children) is layered on with <see cref="WithSectionAssignment"/>.</summary>
    public ProjectGenerationContext BuildContext(
        GccV2BriefDto brief,
        GccV2BrandKitContent? brandKit,
        LlmProviderType provider)
    {
        var fields = ParseBriefFields(brief.RawBriefJson);
        var targetKeyword = string.IsNullOrWhiteSpace(brief.TargetKeyword) ? "this topic" : brief.TargetKeyword;

        var paragraphs = BuildNotesParagraphs(fields, brandKit);
        var siteName = FirstNonEmpty(brandKit?.CompanyName, _company.PublisherName);
        var publisherName = FirstNonEmpty(brandKit?.CompanyName, _company.PublisherName);

        return new ProjectGenerationContext(
            ProjectName: targetKeyword,
            ProjectUrl: FirstNonEmpty(brandKit?.Website, _company.ArticleBaseUrl)!,
            TargetKeyword: targetKeyword,
            Department: "marketing",
            SiteName: siteName!,
            DetectedTone: "Professional, consultative",
            DetectedFocus: targetKeyword,
            CrawledHeadings: [],
            CrawledParagraphs: paragraphs,
            JsonLdStructuredSummary: null,
            KeywordSources: [],
            PeopleAlsoAskQuestions: SplitLines(fields.PaaQuestions).ToList(),
            PublisherName: publisherName!,
            PublisherLogoUrl: _company.PublisherLogoUrl,
            AuthorName: _company.AuthorName,
            ArticleBaseUrl: _company.ArticleBaseUrl,
            BlogBaseUrl: _company.BlogBaseUrl,
            ToolBaseUrl: _company.ToolBaseUrl,
            ImplementerPositioning: _company.ImplementerPositioning,
            Provider: provider,
            UseExactKeywordAsTitle: false,
            DesiredHeadings: null,
            MatchedUseCase: null,
            AudienceSegment: NullIfEmpty(fields.AudienceSegment),
            AudienceDetails: fields.AudienceDetails.Count == 0 ? null : fields.AudienceDetails,
            AudienceNotes: NullIfEmpty(fields.AudienceNotes),
            ContentAngle: NullIfEmpty(fields.Angle),
            PrimaryIntent: NullIfEmpty(fields.PrimaryIntent),
            SecondaryIntent: NullIfEmpty(fields.SecondaryIntent),
            BuyingStage: NullIfEmpty(fields.BuyingStage),
            ToneOfVoice: NullIfEmpty(fields.ToneOfVoice),
            EeatSignals: fields.EeatSignals.Count == 0 ? null : fields.EeatSignals,
            CtaType: NullIfEmpty(fields.CtaType),
            CtaLabel: NullIfEmpty(fields.CtaLabel),
            LengthBand: NullIfEmpty(fields.LengthBand),
            WritingNotes: NullIfEmpty(fields.WritingNotes));
    }

    /// <summary>
    /// Per-section layer: injects this section's assigned <paramref name="job"/> (the PLAN-stage
    /// "problem" | "advance" role, so WRITE cannot let every section restate the same problem/
    /// solution) and its <paramref name="hierarchyChildHeadings"/> subset into the writing notes —
    /// without touching <c>ContentPromptBuilder</c> itself.
    /// </summary>
    public ProjectGenerationContext WithSectionAssignment(
        ProjectGenerationContext context,
        string sectionHeading,
        string? job,
        IReadOnlyList<string>? hierarchyChildHeadings)
    {
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.WritingNotes))
        {
            notes.Add(context.WritingNotes);
        }

        if (!string.IsNullOrWhiteSpace(job))
        {
            notes.Add(job.Equals("problem", StringComparison.OrdinalIgnoreCase)
                ? $"This section (\"{sectionHeading}\") is the ONE place in this piece that establishes the core practitioner problem — do not assume any other section has already stated it."
                : $"This section (\"{sectionHeading}\") must ADVANCE the argument past the problem already established elsewhere in this piece — do not re-open with the same pain point or the same fix already covered in earlier sections. Assume the reader already knows the problem; move to new ground (a distinct sub-topic, workflow step, or consideration).");
        }

        if (hierarchyChildHeadings is { Count: > 0 })
        {
            notes.Add("This section only — must-mention subtopics assigned here (do not cover subtopics assigned to other sections): "
                + string.Join(", ", hierarchyChildHeadings));
        }

        var writingNotes = notes.Count == 0 ? context.WritingNotes : string.Join("\n", notes);
        return context with { WritingNotes = writingNotes };
    }

    private static List<string> BuildNotesParagraphs(BriefFields fields, GccV2BrandKitContent? kit)
    {
        var paragraphs = new List<string>();

        var serpTitles = SplitLines(fields.SerpTitles);
        if (serpTitles.Count > 0)
        {
            paragraphs.Add("Curated SERP titles: " + string.Join(" | ", serpTitles));
        }

        var related = SplitLines(fields.RelatedSearches);
        if (related.Count > 0)
        {
            paragraphs.Add("Related searches: " + string.Join(", ", related));
        }

        if (kit is null)
        {
            return paragraphs;
        }

        if (!string.IsNullOrWhiteSpace(kit.CompanyName))
        {
            paragraphs.Add($"Company: {kit.CompanyName}");
        }

        if (!string.IsNullOrWhiteSpace(kit.CompanyDescription))
        {
            paragraphs.Add($"About the company (from its own site): {kit.CompanyDescription}");
        }

        if (!string.IsNullOrWhiteSpace(kit.PositioningOneLiner))
        {
            paragraphs.Add($"Positioning: {kit.PositioningOneLiner}");
        }

        if (kit.Features.Count > 0)
        {
            paragraphs.Add("Services/features this company actually offers: " + string.Join(", ", kit.Features));
        }

        if (kit.KnowsAbout.Count > 0)
        {
            paragraphs.Add("Topics this company is known for: " + string.Join(", ", kit.KnowsAbout));
        }

        if (kit.VoiceGuidance.Count > 0)
        {
            paragraphs.Add("Brand voice guidance (provisional, derived from the site's own copy): "
                + string.Join(" ", kit.VoiceGuidance));
        }

        if (kit.CtaPhrases.Count > 0)
        {
            paragraphs.Add("Preferred CTA phrasing already used on the site: " + string.Join(", ", kit.CtaPhrases));
        }

        return paragraphs;
    }

    private BriefFields ParseBriefFields(string rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson))
        {
            return new BriefFields();
        }

        try
        {
            var raw = JsonSerializer.Deserialize<RawBrief>(rawBriefJson, BriefJsonOpts);
            if (raw is null)
            {
                return new BriefFields();
            }

            return new BriefFields
            {
                PrimaryIntent = raw.PrimaryIntent ?? "",
                SecondaryIntent = raw.SecondaryIntent ?? "",
                BuyingStage = raw.BuyingStage ?? "",
                AudienceSegment = raw.AudienceSegment ?? "",
                AudienceDetails = raw.AudienceDetails ?? [],
                AudienceNotes = raw.AudienceNotes ?? "",
                Angle = raw.Angle ?? "",
                CtaType = raw.CtaType ?? "",
                CtaLabel = raw.CtaLabel ?? "",
                ToneOfVoice = raw.ToneOfVoice ?? "",
                EeatSignals = raw.EeatSignals ?? [],
                LengthBand = raw.LengthBand ?? "",
                WritingNotes = raw.WritingNotes ?? "",
                SerpTitles = raw.SerpTitles ?? "",
                SerpUrls = raw.SerpUrls ?? "",
                PaaQuestions = raw.PaaQuestions ?? "",
                RelatedSearches = raw.RelatedSearches ?? "",
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse GccV2Brief.RawBriefJson; writing with an empty brief.");
            return new BriefFields();
        }
    }

    private static IReadOnlyList<string> SplitLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Wire shape of <c>ContentBrief</c> (content-creator-v2 frontend's
    /// <c>brief-catalog.ts</c>) as stored verbatim in <see cref="GccV2BriefDto.RawBriefJson"/>.</summary>
    private sealed record RawBrief(
        int? BriefVersion,
        string? PrimaryIntent,
        string? SecondaryIntent,
        string? BuyingStage,
        string? AudienceSegment,
        List<string>? AudienceDetails,
        string? AudienceNotes,
        string? Angle,
        string? CtaType,
        string? CtaLabel,
        string? ToneOfVoice,
        List<string>? EeatSignals,
        string? LengthBand,
        string? WritingNotes,
        string? SerpTitles,
        string? SerpUrls,
        string? PaaQuestions,
        string? RelatedSearches);

    private sealed class BriefFields
    {
        public string PrimaryIntent { get; init; } = "";
        public string SecondaryIntent { get; init; } = "";
        public string BuyingStage { get; init; } = "";
        public string AudienceSegment { get; init; } = "";
        public List<string> AudienceDetails { get; init; } = [];
        public string AudienceNotes { get; init; } = "";
        public string Angle { get; init; } = "";
        public string CtaType { get; init; } = "";
        public string CtaLabel { get; init; } = "";
        public string ToneOfVoice { get; init; } = "";
        public List<string> EeatSignals { get; init; } = [];
        public string LengthBand { get; init; } = "";
        public string WritingNotes { get; init; } = "";
        public string SerpTitles { get; init; } = "";
        public string SerpUrls { get; init; } = "";
        public string PaaQuestions { get; init; } = "";
        public string RelatedSearches { get; init; } = "";
    }
}
