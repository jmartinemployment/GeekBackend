using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;
using GeekAPI.Services.Rag;

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
    string? SiteUrl,
    Guid? ProjectSiteCrawlRunId,
    string? SiteSectionJson,
    GccV2BrandKitContent? BrandKit,
    JsonElement RawBrief)
{
    public const string CurrentVersion = "gcc-v2-generation-brief.v1";

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
    public const string O1Pro = "o1-pro";
    public const string O3 = "o3";
    public const string StandardMultimodal = "gpt-4o";

    private static readonly IReadOnlyDictionary<ContentGenerationStage, IReadOnlySet<string>> Approved =
        new Dictionary<ContentGenerationStage, IReadOnlySet<string>>
        {
            [ContentGenerationStage.Research] = new HashSet<string>([O3], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.Outline] = new HashSet<string>([O1Pro, O3], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.Section] = new HashSet<string>([O3, O1Pro], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.Repair] = new HashSet<string>([O3, O1Pro], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.Validation] = new HashSet<string>([O3, O1Pro], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.FinalSynthesis] = new HashSet<string>([O1Pro, O3], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.Complete] = new HashSet<string>([O3, O1Pro], StringComparer.OrdinalIgnoreCase),
            [ContentGenerationStage.ImagePrompt] = new HashSet<string>([O3], StringComparer.OrdinalIgnoreCase),
        };

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ApprovedStageModels { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["researchPlanning"] = [O3],
            ["outline"] = [O1Pro, O3],
            ["section"] = [O3, O1Pro],
            ["repair"] = [O3, O1Pro],
            ["validation"] = [O3, O1Pro],
            ["finalSynthesis"] = [O1Pro, O3],
            ["complete"] = [O3, O1Pro],
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
        if (stage == ContentGenerationStage.ImagePrompt) return O3;
        if (preset == ContentModelPreset.O3Only) return O3;
        return stage is ContentGenerationStage.Outline or ContentGenerationStage.FinalSynthesis ? O1Pro : O3;
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
    ContentModelSelection ModelSelection)
{
    public string Stage => ContentModelPolicy.ProducerStage(ModelSelection.Stage);
    public string EffectiveModel => ModelSelection.EffectiveModel;
    public string? AttemptId => null;
}

public sealed record GccV2ResearchEvidenceManifest(
    string Version,
    IReadOnlyList<RagGenerateSourceDto> Sources,
    IReadOnlyList<RagCitationDto> VerifiedCitations,
    IReadOnlyList<string> EvidenceGaps,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Warnings,
    DateTimeOffset AssembledAtUtc)
{
    public bool Ready => EvidenceGaps.Count == 0 && Conflicts.Count == 0;
}
