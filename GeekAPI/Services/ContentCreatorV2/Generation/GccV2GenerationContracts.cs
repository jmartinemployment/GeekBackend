using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;
using GeekAPI.Services.Rag;
using GeekAPI.Services.ContentCreatorV2.Write;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed record GccV2RagRoute(string ContentType, string WritingIntent, RagRetrievalFamily Family, bool IsImagePrompt);

public static class GccV2ContentTypeRagMapper
{
    public static readonly IReadOnlyList<string> CanonicalContentTypes =
    [
        "pillar", "blog", "guide", "tech-article", "case-study", "whitepaper", "listicle",
        "comparison", "alternatives", "ads", "social", "email", "linkedin-document",
        "tool", "service", "local", "image-prompt",
    ];

    public static GccV2RagRoute Map(string? contentType)
    {
        var normalized = (contentType ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "pillar" or "blog" or "guide" or "whitepaper" or "listicle" or "tool" or "service" or "local" =>
                new(normalized, RagWritingIntents.TechnicalArticle, RagRetrievalFamily.LongForm, false),
            "tech-article" =>
                new(normalized, RagWritingIntents.TechnicalArticle, RagRetrievalFamily.LongForm, false),
            "case-study" =>
                new(normalized, RagWritingIntents.CaseStudy, RagRetrievalFamily.LongForm, false),
            "comparison" or "alternatives" =>
                new(normalized, RagWritingIntents.CompetitiveBattlecard, RagRetrievalFamily.Battlecard, false),
            "ads" =>
                new(normalized, RagWritingIntents.SocialAd, RagRetrievalFamily.ShortForm, false),
            "social" or "email" =>
                new(normalized, RagWritingIntents.ShortForm, RagRetrievalFamily.ShortForm, false),
            "linkedin-document" or "linkedin-carousel" =>
                new(normalized, RagWritingIntents.PitchSlides, RagRetrievalFamily.Slides, false),
            "image-prompt" =>
                new(normalized, RagWritingIntents.ShortForm, RagRetrievalFamily.ShortForm, true),
            _ => throw new InvalidOperationException($"Unsupported canonical content type for RAG: {contentType}."),
        };
    }
}

public sealed record GccV2GenerationBrief(
    string Version,
    Guid BriefId,
    Guid CreateId,
    string ContentType,
    string Title,
    string TargetKeyword,
    string? PrimaryIntent,
    string? SecondaryIntent,
    string? BuyingStage,
    string? AudienceSegment,
    string? AudienceNotes,
    string? ToneOfVoice,
    string? Angle,
    string? WritingNotes,
    string? CtaType,
    string? CtaLabel,
    string? LengthBand,
    IReadOnlyList<string> PaaQuestions,
    IReadOnlyList<string> RequiredTopics,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<string> CompetitorUrls,
    IReadOnlyList<string> OperatorTools,
    IReadOnlyList<string> TargetEntities,
    IReadOnlyList<Guid> PartnerSourceRunIds,
    IReadOnlyList<Guid> CompetitorSourceRunIds,
    IReadOnlyList<RagAdTemplateDto> AdTemplates,
    string? SiteUrl,
    Guid? ProjectSiteCrawlRunId,
    string? SiteSectionJson,
    GccV2BrandKitContent? BrandKit,
    JsonElement RawBrief)
{
    public const string CurrentVersion = "gcc-v2-generation-brief.v1";

    /// <summary>First partner run when present (legacy singular wire field).</summary>
    public Guid? PartnerSourceRunId =>
        PartnerSourceRunIds.Count > 0 ? PartnerSourceRunIds[0] : null;

    /// <summary>First competitor run when present (legacy singular wire field).</summary>
    public Guid? CompetitorSourceRunId =>
        CompetitorSourceRunIds.Count > 0 ? CompetitorSourceRunIds[0] : null;

    public JsonElement ToCanonicalBrief()
    {
        var payload = new
        {
            version = Version,
            title = Title,
            targetKeyword = TargetKeyword,
            contentType = ContentType,
            primaryIntent = PrimaryIntent,
            secondaryIntent = SecondaryIntent,
            buyingStage = BuyingStage,
            audienceSegment = AudienceSegment,
            audienceNotes = AudienceNotes,
            toneOfVoice = ToneOfVoice,
            angle = Angle,
            writingNotes = WritingNotes,
            ctaType = CtaType,
            ctaLabel = CtaLabel,
            lengthBand = LengthBand,
            paaQuestions = PaaQuestions,
            requiredTopics = RequiredTopics,
            exclusions = Exclusions,
            competitorUrls = CompetitorUrls,
            operatorTools = OperatorTools,
            targetEntities = TargetEntities,
            partnerSourceRunIds = PartnerSourceRunIds,
            competitorSourceRunIds = CompetitorSourceRunIds,
            // Singular kept for older readers; equals first of each list.
            partnerSourceRunId = PartnerSourceRunId,
            competitorSourceRunId = CompetitorSourceRunId,
            adTemplateIds = AdTemplates.Select(template => template.Id).ToList(),
            siteUrl = SiteUrl,
            projectSiteCrawlRunId = ProjectSiteCrawlRunId,
            siteSection = ParseJsonOrNull(SiteSectionJson),
            brandKit = BrandKit,
            rawBrief = RawBrief,
        };
        return JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static JsonElement? ParseJsonOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }
}

public static class GccV2GenerationBriefAssembler
{
    public static GccV2GenerationBrief Assemble(
        GccV2JobDto job,
        GccV2BriefDto brief,
        GccV2CreateDto? create,
        GccV2BrandKitContent? brandKit)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(brief.RawBriefJson) ? "{}" : brief.RawBriefJson);
        var root = doc.RootElement;
        var contentType = string.IsNullOrWhiteSpace(job.ContentType) ? brief.ContentType : job.ContentType;
        return new GccV2GenerationBrief(
            GccV2GenerationBrief.CurrentVersion,
            brief.Id,
            brief.CreateId,
            contentType.Trim().ToLowerInvariant(),
            ReadString(root, "title") ?? create?.Title ?? brief.TargetKeyword,
            brief.TargetKeyword,
            ReadString(root, "primaryIntent"),
            ReadString(root, "secondaryIntent"),
            ReadString(root, "buyingStage"),
            ReadString(root, "audienceSegment"),
            ReadString(root, "audienceNotes"),
            ReadString(root, "toneOfVoice"),
            ReadString(root, "angle"),
            ReadString(root, "writingNotes"),
            ReadString(root, "ctaType"),
            ReadString(root, "ctaLabel"),
            ReadString(root, "lengthBand"),
            ReadStrings(root, "paaQuestions"),
            ReadStrings(root, "requiredTopics"),
            ReadStrings(root, "exclusions"),
            ReadStrings(root, "competitorUrls"),
            ReadToolStrings(root, "operatorTools"),
            ReadStrings(root, "targetEntities"),
            ReadRunIds(
                root,
                pluralNames: ["partnerSourceRunIds", "partnerRunIds"],
                singularNames: ["partnerSourceRunId", "partnerRunId", "partnerCrawlRunId"]),
            ReadRunIds(
                root,
                pluralNames: ["competitorSourceRunIds", "competitorRunIds"],
                singularNames: ["competitorSourceRunId", "competitorRunId", "competitorCrawlRunId"]),
            ReadAdTemplates(root),
            create?.SiteUrl,
            job.ProjectSiteCrawlRunId ?? create?.ProjectSiteCrawlRunId,
            create?.SiteSectionJson,
            brandKit,
            root.Clone());
    }

    private static string? ReadString(JsonElement root, string name) =>
        TryGet(root, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static IReadOnlyList<string> ReadStrings(JsonElement root, string name)
    {
        if (!TryGet(root, name, out var value)) return [];
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString()!.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray()
            .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null)
            .Where(v => !string.IsNullOrWhiteSpace(v)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ReadToolStrings(JsonElement root, string name)
    {
        if (!TryGet(root, name, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Select(v =>
            v.ValueKind == JsonValueKind.String ? v.GetString()
            : v.ValueKind == JsonValueKind.Object
                ? ReadString(v, "name") ?? ReadString(v, "url") ?? ReadString(v, "href")
                : null)
            .Where(v => !string.IsNullOrWhiteSpace(v)).Cast<string>().ToList();
    }

    private static Guid? ReadGuid(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (ReadString(root, name) is { } raw && Guid.TryParse(raw, out var value))
                return value;
        return null;
    }

    /// <summary>
    /// Union of plural Guid arrays and legacy singular Guid fields (order preserved, distinct).
    /// </summary>
    private static IReadOnlyList<Guid> ReadRunIds(
        JsonElement root,
        string[] pluralNames,
        string[] singularNames)
    {
        var ids = new List<Guid>();
        var seen = new HashSet<Guid>();

        void Add(Guid id)
        {
            if (id == Guid.Empty || !seen.Add(id)) return;
            ids.Add(id);
        }

        foreach (var name in pluralNames)
        {
            if (!TryGet(root, name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String
                        && Guid.TryParse(item.GetString(), out var fromArray))
                        Add(fromArray);
                }
            }
            else if (value.ValueKind == JsonValueKind.String
                     && Guid.TryParse(value.GetString(), out var fromString))
            {
                Add(fromString);
            }
        }

        foreach (var name in singularNames)
        {
            if (ReadString(root, name) is { } raw && Guid.TryParse(raw, out var singular))
                Add(singular);
        }

        return ids;
    }

    private static IReadOnlyList<RagAdTemplateDto> ReadAdTemplates(JsonElement root)
    {
        if (!TryGet(root, "ragAdTemplates", out var value) || value.ValueKind != JsonValueKind.Array)
            return [];
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new RagAdTemplateDto
            {
                Id = ReadString(item, "id") ?? "",
                Name = ReadString(item, "name") ?? "",
                Channel = ReadString(item, "channel"),
                Framework = ReadString(item, "framework"),
                Body = ReadString(item, "body") ?? "",
            })
            .Where(template => !string.IsNullOrWhiteSpace(template.Id)
                               && !string.IsNullOrWhiteSpace(template.Body))
            .DistinctBy(template => template.Id, StringComparer.Ordinal)
            .Take(5)
            .ToList();
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}

public enum ContentGenerationStage { Research, Outline, Section, Repair, Validation, FinalSynthesis, Complete, ImagePrompt }
public enum ContentModelPreset { BestQuality, O3Only, Custom }

public sealed record ContentModelSelection(
    string PolicyVersion,
    ContentModelPreset Preset,
    ContentGenerationStage Stage,
    string RequestedModel,
    string EffectiveModel,
    bool IsOverride,
    string? OperatorUserId,
    DateTimeOffset? OverriddenAtUtc,
    string? OverrideReason);

public sealed class ContentModelPolicy
{
    public const string CurrentVersion = "content-model-policy.v1";
    /// <summary>
    /// Retained for contract compatibility only. NOT approved for any stage: the provider speaks
    /// /v1/chat/completions and OpenAI serves this model only at /v1/responses.
    /// </summary>
    public const string O1Pro = "o1-pro";
    public const string O3 = "o3";

    /// <summary>
    /// Default for every stage. Cheapest of the family, serves /v1/chat/completions, supports prompt
    /// caching, and suits factual RAG extraction — search, pull a direct answer, cite a source.
    /// Step up to <see cref="O3"/> per stage when a piece needs more depth.
    /// </summary>
    public const string O3Mini = "o3-mini";
    /// <summary>Cheapest approved model. Remains approved for every stage; no longer the default.</summary>
    public const string Gpt4oMini = "gpt-4o-mini";
    public const string StandardMultimodal = "gpt-4o";

    private static readonly IReadOnlyDictionary<ContentGenerationStage, IReadOnlySet<string>> Approved =
        new Dictionary<ContentGenerationStage, IReadOnlySet<string>>
        {
            [ContentGenerationStage.Research] = new HashSet<string>([Gpt4oMini, O3Mini, O3], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.Outline] = new HashSet<string>([Gpt4oMini, O3Mini, O3], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.Section] = new HashSet<string>([Gpt4oMini, O3Mini, O3], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.Repair] = new HashSet<string>([Gpt4oMini, O3Mini, O3], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.Validation] = new HashSet<string>([Gpt4oMini, O3Mini, O3], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.FinalSynthesis] = new HashSet<string>([Gpt4oMini, O3Mini, O3], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.Complete] = new HashSet<string>([Gpt4oMini, O3Mini, O3], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.ImagePrompt] = new HashSet<string>([Gpt4oMini, O3Mini, O3], StringComparer.OrdinalIgnoreCase),
        };

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ApprovedStageModels { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["researchPlanning"] = [Gpt4oMini, O3Mini, O3],
            ["outline"] = [Gpt4oMini, O3Mini, O3],
            ["section"] = [Gpt4oMini, O3Mini, O3],
            ["repair"] = [Gpt4oMini, O3Mini, O3],
            ["validation"] = [Gpt4oMini, O3Mini, O3],
            ["finalSynthesis"] = [Gpt4oMini, O3Mini, O3],
            ["complete"] = [Gpt4oMini, O3Mini, O3],
        };

    public ContentModelSelection Select(
        ContentGenerationStage stage,
        GccV2GenerationBrief brief,
        GccV2JobModelPolicyOverride? jobOverride = null)
    {
        var policyObject = TryGet(brief.RawBrief, "modelPolicy", out var nested)
                           && nested.ValueKind == JsonValueKind.Object
            ? nested
            : brief.RawBrief;
        var preset = ParsePreset(policyObject);
        var policyVersion = ReadString(policyObject, "version") ?? ReadString(policyObject, "modelPolicyVersion");
        if (!string.IsNullOrWhiteSpace(policyVersion)
            && !string.Equals(policyVersion, CurrentVersion, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Unsupported model policy version '{policyVersion}'. Expected '{CurrentVersion}'.");
        if (preset != ContentModelPreset.BestQuality
            && (!TryGet(policyObject, "downgradeConfirmed", out var confirmed)
                || confirmed.ValueKind != JsonValueKind.True))
            throw new InvalidOperationException(
                "Non-best model policies require downgradeConfirmed=true.");
        if (jobOverride is not null)
        {
            if (!string.Equals(jobOverride.Version, CurrentVersion, StringComparison.Ordinal)
                || !jobOverride.DowngradeConfirmed)
                throw new InvalidOperationException("The job-scoped model override is invalid or unconfirmed.");
            preset = ContentModelPreset.Custom;
        }
        var requested = DefaultFor(stage, preset);
        var configured = Environment.GetEnvironmentVariable($"GEEK_CONTENT_MODEL_{stage.ToString().ToUpperInvariant()}")?.Trim();
        if (!string.IsNullOrWhiteSpace(configured)) requested = configured;

        var overrideModel = jobOverride?.StageModels
            .FirstOrDefault(pair => StageAliases(stage).Contains(pair.Key, StringComparer.OrdinalIgnoreCase)).Value
            ?? ReadOverride(policyObject, stage);
        if (preset == ContentModelPreset.Custom && string.IsNullOrWhiteSpace(overrideModel))
            throw new InvalidOperationException(
                $"Custom model policy has no approved model for stage '{ProducerStage(stage)}'.");
        var effective = overrideModel ?? requested;
        if (!Approved[stage].Contains(effective))
            throw new InvalidOperationException(
                $"Model '{effective}' is not approved for {stage}. Approved models: {string.Join(", ", Approved[stage])}.");

        return new ContentModelSelection(
            CurrentVersion, preset, stage, requested, effective, overrideModel is not null,
            jobOverride?.OperatorUserId
                ?? ReadString(policyObject, "operatorUserId")
                ?? ReadString(brief.RawBrief, "modelOverrideOperatorUserId"),
            jobOverride?.Timestamp
                ?? ReadDate(policyObject, "overriddenAtUtc")
                ?? ReadDate(brief.RawBrief, "modelOverrideTimestamp"),
            jobOverride?.Reason
                ?? ReadString(policyObject, "downgradeReason")
                ?? ReadString(brief.RawBrief, "modelOverrideReason"));
    }

    private static string DefaultFor(ContentGenerationStage stage, ContentModelPreset preset)
    {
        // o3-mini for every stage by default. Outline and FinalSynthesis used to default to o1-pro,
        // which this app cannot call at all: the provider posts to /v1/chat/completions and OpenAI
        // serves o1-pro only at /v1/responses, so those stages 404'd before reaching a model. o3-mini
        // is also ~75x cheaper on input than o1-pro ($2 vs $150 per 1M), supports prompt caching, and
        // suits the factual extraction this pipeline does against verified RAG corpus text.
        //
        // The O3Only preset still pins full o3, so an operator who wants the heavier reasoning model
        // for a given create can ask for it explicitly.
        _ = stage;
        // o3-mini is the default: the o3 family is what suits factual RAG extraction -- search, pull
        // a direct answer, cite a source -- which is what every stage here does against verified
        // corpus text derived from the crawler's typed blocks.
        //
        // The gpt-4o-mini cost posture that preceded this was justified by output being unusable
        // while v1's prompt layer was missing. The v1 restore has landed, so output is worth judging
        // and the cheaper model is no longer the right trade.
        //
        // Spending is gated separately: LlmProviders:Enabled=false stops the v1/Workflow path and
        // ContentCreatorV2:DraftingEnabled=false stops this one, both before the first paid call.
        // Note o3-mini bills reasoning tokens that do not appear in the output, so it is not a
        // like-for-like swap on cost even at comparable per-token rates.
        return preset == ContentModelPreset.O3Only ? O3 : O3Mini;
    }

    private static ContentModelPreset ParsePreset(JsonElement root) =>
        (ReadString(root, "preset") ?? ReadString(root, "modelPolicyPreset"))?.ToLowerInvariant() switch
        {
            "o3-only" or "o3_only" => ContentModelPreset.O3Only,
            "custom" => ContentModelPreset.Custom,
            _ => ContentModelPreset.BestQuality,
        };

    private static string? ReadOverride(JsonElement root, ContentGenerationStage stage)
    {
        if ((!TryGet(root, "stageModels", out var overrides)
             && !TryGet(root, "modelPolicyOverrides", out overrides))
            || overrides.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var key in StageAliases(stage))
        {
            var model = ReadString(overrides, key)?.Trim();
            if (!string.IsNullOrWhiteSpace(model)) return model;
        }
        return null;
    }

    public static string ProducerStage(ContentGenerationStage stage) => stage switch
    {
        ContentGenerationStage.Research => "researchPlanning",
        ContentGenerationStage.Outline => "outline",
        ContentGenerationStage.Section => "section",
        ContentGenerationStage.Repair => "repair",
        ContentGenerationStage.Validation => "validation",
        ContentGenerationStage.FinalSynthesis => "finalSynthesis",
        ContentGenerationStage.Complete => "complete",
        ContentGenerationStage.ImagePrompt => "complete",
        _ => "complete",
    };

    public static string PresetValue(ContentModelPreset preset) => preset switch
    {
        ContentModelPreset.O3Only => "o3-only",
        ContentModelPreset.Custom => "custom",
        _ => "best-quality",
    };

    public static bool IsApproved(string stage, string model) =>
        ApprovedStageModels.TryGetValue(stage, out var models)
        && models.Contains(model, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string>? ProducerOverrides(GccV2GenerationBrief brief)
    {
        var root = TryGet(brief.RawBrief, "modelPolicy", out var nested)
                   && nested.ValueKind == JsonValueKind.Object
            ? nested
            : brief.RawBrief;
        if ((!TryGet(root, "stageModels", out var stageModels)
             && !TryGet(root, "modelPolicyOverrides", out stageModels))
            || stageModels.ValueKind != JsonValueKind.Object)
            return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var stage in ApprovedStageModels.Keys)
        {
            var value = ReadString(stageModels, stage);
            if (!string.IsNullOrWhiteSpace(value)) result[stage] = value.Trim();
        }
        return result.Count == 0 ? null : result;
    }

    public static IReadOnlyDictionary<string, string>? ProducerOverridesForRequest(
        GccV2GenerationBrief brief,
        ContentModelSelection selection,
        string generationStage,
        GccV2JobModelPolicyOverride? jobOverride = null)
    {
        if (selection.Preset != ContentModelPreset.Custom) return null;
        var result = new Dictionary<string, string>(
            jobOverride?.StageModels
            ?? ProducerOverrides(brief)
            ?? new Dictionary<string, string>(),
            StringComparer.Ordinal);
        result[generationStage] = selection.EffectiveModel;
        return result;
    }

    private static IReadOnlyList<string> StageAliases(ContentGenerationStage stage) =>
        stage switch
        {
            ContentGenerationStage.Research => ["researchPlanning", "research", "Research"],
            ContentGenerationStage.FinalSynthesis => ["finalSynthesis", "final-synthesis", "FinalSynthesis"],
            ContentGenerationStage.ImagePrompt => ["complete", "imagePrompt", "image-prompt", "ImagePrompt"],
            _ => [ProducerStage(stage), stage.ToString()],
        };

    private static DateTimeOffset? ReadDate(JsonElement root, string name) =>
        DateTimeOffset.TryParse(ReadString(root, name), out var parsed) ? parsed : null;

    private static string? ReadString(JsonElement root, string name) =>
        TryGet(root, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var property in root.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
        value = default;
        return false;
    }
}

public sealed record GccV2GenerationProvenance(
    string BriefVersion,
    string ModelPolicyVersion,
    string PromptVersion,
    string RequestedModel,
    string? ModelUsed,
    string? RetrievalStrategy,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> Warnings,
    long LatencyMs,
    ContentModelSelection ModelSelection,
    string AttemptId = "",
    GccV2SkillProvenance? Skills = null,
    string? ProducerExecutionVersion = null,
    RagAgentExecutionProvenanceDto? AgentExecution = null,
    string? NegotiationReason = null)
{
    public string Stage => ContentModelPolicy.ProducerStage(ModelSelection.Stage);
    public string EffectiveModel => ModelSelection.EffectiveModel;
}

public sealed record GccV2SkillProvenance(
    string EnvelopeVersion,
    string CatalogVersion,
    string SnapshotHash,
    string Stage,
    IReadOnlyList<string> SkillVersions);

public sealed record GccV2ResearchEvidenceManifest(
    string Version,
    IReadOnlyList<CreateLibraryDraftSourceDto> Sources,
    IReadOnlyList<RagCitationDto> VerifiedCitations,
    IReadOnlyList<string> EvidenceGaps,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Warnings,
    DateTimeOffset AssembledAtUtc,
    IReadOnlyList<GccV2EvidenceIndexReadiness>? IndexReadiness = null,
    IReadOnlyList<RagCitationDto>? CandidateQuotes = null,
    IReadOnlyList<string>? InternalLinkOpportunities = null)
{
    public const string CurrentVersion = "gcc-v2-research-evidence-manifest.v1";

    /// <summary>
    /// Pre-PLAN gate: required project-site + partner + competitor evidence present and no hard conflicts.
    /// Missing partner/competitor run IDs are <see cref="EvidenceGaps"/> (Appendix A — every Create).
    /// </summary>
    public bool Ready => EvidenceGaps.Count == 0 && Conflicts.Count == 0;
}

/// <summary>Index readiness for one evidence run/source before PLAN.</summary>
public sealed record GccV2EvidenceIndexReadiness(
    string Role,
    Guid? RunId,
    string? Label,
    bool Indexed,
    string? Detail);

/// <summary>
/// Partner/competitor identity with <b>role per request</b> (same company may be partner
/// on one create and competitor on another). Does not mutate the durable ResearchEntity row.
/// </summary>
public sealed record GccV2ResearchEntityRef(
    Guid? EntityId,
    string DisplayName,
    string Role,
    string? PrimaryUrl,
    string? StableKey)
{
    public const string RolePartner = "partner";
    public const string RoleCompetitor = "competitor";

    public static GccV2ResearchEntityRef FromStored(
        Guid id,
        string name,
        string requestRole,
        string? primaryUrl)
    {
        var role = NormalizeRole(requestRole);
        return new(
            id,
            name.Trim(),
            role,
            string.IsNullOrWhiteSpace(primaryUrl) ? null : primaryUrl.Trim(),
            StableKeyFrom(primaryUrl, id));
    }

    public static GccV2ResearchEntityRef FromUrl(string url, string requestRole, string? displayName = null)
    {
        var trimmed = url.Trim();
        return new(
            null,
            string.IsNullOrWhiteSpace(displayName) ? trimmed : displayName.Trim(),
            NormalizeRole(requestRole),
            trimmed,
            StableKeyFrom(trimmed, null));
    }

    public static string NormalizeRole(string? role) =>
        string.Equals(role?.Trim(), RoleCompetitor, StringComparison.OrdinalIgnoreCase)
            ? RoleCompetitor
            : RolePartner;

    public static string StableKeyFrom(string? primaryUrl, Guid? entityId)
    {
        if (!string.IsNullOrWhiteSpace(primaryUrl)
            && Uri.TryCreate(primaryUrl.Trim(), UriKind.Absolute, out var uri))
            return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return entityId is Guid id ? id.ToString("D") : (primaryUrl ?? "").Trim().ToLowerInvariant();
    }
}

/// <summary>Assembles an inspectable evidence manifest from the generation brief before PLAN.</summary>
public static class GccV2PrePlanEvidenceManifestAssembler
{
    public static GccV2ResearchEvidenceManifest Assemble(GccV2GenerationBrief brief)
    {
        var gaps = new List<string>();
        var warnings = new List<string>();
        var readiness = new List<GccV2EvidenceIndexReadiness>();
        var internalLinks = new List<string>();

        if (brief.ProjectSiteCrawlRunId is Guid siteRun)
        {
            readiness.Add(new("project_site", siteRun, brief.SiteUrl, Indexed: true,
                "Project-site crawl run id present on create/job."));
        }
        else
        {
            gaps.Add("Missing required project-site crawl run id.");
            readiness.Add(new("project_site", null, brief.SiteUrl, Indexed: false,
                "No project-site crawl run bound to this create."));
        }

        var contentType = (brief.ContentType ?? "").Trim().ToLowerInvariant();
        // Appendix A: every Create requires bound partner + competitor crawl runs (fail closed).

        if (brief.PartnerSourceRunIds.Count > 0)
        {
            foreach (var partnerRun in brief.PartnerSourceRunIds)
            {
                readiness.Add(new(GccV2ResearchEntityRef.RolePartner, partnerRun,
                    brief.OperatorTools.FirstOrDefault(), Indexed: true,
                    "Partner run id present on brief."));
            }
        }
        else
        {
            gaps.Add(PartnerFailClosedMessage(contentType));
            readiness.Add(new(GccV2ResearchEntityRef.RolePartner, null,
                brief.OperatorTools.FirstOrDefault(), Indexed: false,
                "Partner crawl run required for every Create — bind indexed partner run(s) before PLAN."));
        }

        if (brief.CompetitorSourceRunIds.Count > 0)
        {
            foreach (var competitorRun in brief.CompetitorSourceRunIds)
            {
                readiness.Add(new(GccV2ResearchEntityRef.RoleCompetitor, competitorRun,
                    brief.CompetitorUrls.FirstOrDefault(), Indexed: true,
                    "Competitor run id present on brief."));
            }
        }
        else if (RequiresCompetitorRunFailClosed(contentType, brief.CompetitorUrls.Count))
        {
            gaps.Add(CompetitorFailClosedMessage(contentType));
            readiness.Add(new(GccV2ResearchEntityRef.RoleCompetitor, null,
                brief.CompetitorUrls.FirstOrDefault(), Indexed: false,
                "Competitor crawl run required for comparison and alternatives content — "
                + "bind indexed competitor run(s) before PLAN."));
        }

        if (!string.IsNullOrWhiteSpace(brief.SiteSectionJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(brief.SiteSectionJson);
                if (doc.RootElement.TryGetProperty("relatedPages", out var pages)
                    && pages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var page in pages.EnumerateArray())
                    {
                        var url = page.ValueKind == JsonValueKind.Object
                                  && page.TryGetProperty("url", out var urlEl)
                            ? urlEl.GetString()
                            : page.ValueKind == JsonValueKind.String ? page.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(url))
                            internalLinks.Add(url!);
                    }
                }
            }
            catch (JsonException)
            {
                warnings.Add("Site section JSON could not be parsed for internal-link opportunities.");
            }
        }

        return new GccV2ResearchEvidenceManifest(
            GccV2ResearchEvidenceManifest.CurrentVersion,
            Sources: [],
            VerifiedCitations: [],
            EvidenceGaps: gaps,
            Conflicts: [],
            Warnings: warnings,
            AssembledAtUtc: DateTimeOffset.UtcNow,
            IndexReadiness: readiness,
            CandidateQuotes: [],
            InternalLinkOpportunities: internalLinks);
    }

    /// <summary>
    /// Appendix A: partner crawl run required on every Create (fail closed).
    /// <paramref name="operatorToolCount"/> retained for call-site compatibility.
    /// </summary>
    internal static bool RequiresPartnerRunFailClosed(string contentType, int operatorToolCount)
    {
        _ = contentType;
        _ = operatorToolCount;
        return true;
    }

    /// <summary>
    /// Competitor evidence is NEVER required. The operator sells implementation of partner tools;
    /// competitors provide a slim slice — positioning and honest mention — and no Create should be
    /// blocked for lack of one. Partner evidence remains mandatory.
    /// Parameters retained for call-site compatibility.
    /// </summary>
    internal static bool RequiresCompetitorRunFailClosed(string contentType, int competitorUrlCount)
    {
        _ = contentType;
        _ = competitorUrlCount;
        return false;
    }

    private static string PartnerFailClosedMessage(string contentType) => contentType switch
    {
        "tool" =>
            "Tool pages require indexed partner crawl run(s) — bind partnerSourceRunIds (re-crawl partners if needed).",
        "ads" =>
            "Ads require indexed partner crawl run(s) — bind partnerSourceRunIds before PLAN.",
        "comparison" or "alternatives" =>
            "Comparison/alternatives require indexed partner crawl run(s) — bind partnerSourceRunIds before PLAN.",
        _ =>
            "Partner crawl run is required for every Create before PLAN — bind partnerSourceRunIds.",
    };

    private static string CompetitorFailClosedMessage(string contentType) => contentType switch
    {
        "comparison" or "alternatives" =>
            "Comparison/alternatives require indexed competitor crawl run(s) — bind competitorSourceRunIds before PLAN.",
        // Unreachable while RequiresCompetitorRunFailClosed returns false. Kept accurate so it does
        // not reintroduce the "required for every Create" claim if the gate is ever re-enabled.
        _ =>
            "Competitor crawl run not bound — optional, so this does not block PLAN.",
    };
}
