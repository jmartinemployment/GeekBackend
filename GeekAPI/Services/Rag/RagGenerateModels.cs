using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.ContentCreatorV2.Generation;

namespace GeekAPI.Services.Rag;

public sealed class RagGenerateRequest
{
    public string WritingIntent { get; set; } = "";
    public string Topic { get; set; } = "";
    public List<string>? TargetEntities { get; set; }
    public Guid? PartnerRunId { get; set; }
    public Guid? CompetitorRunId { get; set; }

    /// <summary>Phase D2 — operator-selected ad template exemplars (owned by content-creator-v2).</summary>
    public List<RagAdTemplateDto>? AdTemplates { get; set; }

    /// <summary>Optional template ids when Rag ad-template index is available.</summary>
    public List<string>? TemplateIds { get; set; }
    public string GenerationStage { get; set; } = "complete";
    public List<RagOutlineSectionDto>? Outline { get; set; }
    public string? SectionKey { get; set; }
    public string? SectionHeading { get; set; }
    public string? SectionBrief { get; set; }
    public List<string>? CompletedSectionSummaries { get; set; }
    public string? DraftContent { get; set; }
    public IReadOnlyList<RagGenerateSourceDto>? Sources { get; set; }
    public JsonElement? CanonicalBrief { get; set; }
    public string? ModelPolicyPreset { get; set; }
    public string? ModelPolicyVersion { get; set; }
    public IReadOnlyDictionary<string, string>? StageModelOverrides { get; set; }
    public string ExecutionVersion { get; set; } = RagProducerCapabilities.RequiredExecutionVersion;
    public string AttemptId { get; set; } = Guid.NewGuid().ToString("D");
    public GccV2SkillExecutionSnapshot? SkillExecution { get; set; }
    /// <summary>Expected effective model, used locally to reject producer substitution.</summary>
    public string? RequestedModel { get; set; }
    /// <summary>Canonical jobs require quote-verified RAG and may not use the local one-shot writer.</summary>
    public bool RequireCiteable { get; set; }
}

public sealed class RagOutlineSectionDto
{
    public string Key { get; set; } = "";
    public string Heading { get; set; } = "";
    public string Brief { get; set; } = "";
    public IReadOnlyList<string> EvidenceIds { get; set; } = [];
}

public sealed class RagGenerateProvenanceDto
{
    public string? GenerationStage { get; init; }
    public string? ModelUsed { get; init; }
    public string? ModelPolicyPreset { get; init; }
    public string? ModelPolicyVersion { get; init; }
    public string? PromptVersion { get; init; }
    public string? Retrieval { get; init; }
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
    public string? SpecialistExecutor { get; init; }
    public string? SpecialistExecutorVersion { get; init; }
    public string? ExecutionVersion { get; init; }
    public string? AttemptId { get; init; }
    public RagSkillProvenanceDto? Skills { get; init; }
}

public static class RagProducerCapabilities
{
    public const string RequiredExecutionVersion = "rag-generate.v2";
    public const string RequiredSkillEnvelopeVersion = GccV2SkillExecutionSnapshot.CurrentEnvelopeVersion;
    public const string RequiredSpecialistExecutorVersion = "bounded-specialists.v1";
    public static readonly IReadOnlyList<string> RequiredSpecialists =
        ["researchPlanning", "outline", "section", "finalSynthesis", "validation", "repair"];
}

public sealed class RagSkillProvenanceDto
{
    public string EnvelopeVersion { get; init; } = "";
    public string CatalogVersion { get; init; } = "";
    public string SnapshotHash { get; init; } = "";
    public string Stage { get; init; } = "";
    public IReadOnlyList<string> SkillVersions { get; init; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<RagValidationIssueCategory>))]
public enum RagValidationIssueCategory
{
    [JsonStringEnumMemberName("unsupportedClaim")]
    UnsupportedClaim,
    [JsonStringEnumMemberName("sourceConflict")]
    SourceConflict,
    [JsonStringEnumMemberName("briefAlignment")]
    BriefAlignment,
    [JsonStringEnumMemberName("brandVoice")]
    BrandVoice,
    [JsonStringEnumMemberName("originalityRepetition")]
    OriginalityRepetition,
    [JsonStringEnumMemberName("usefulness")]
    Usefulness,
    [JsonStringEnumMemberName("cta")]
    Cta,
    [JsonStringEnumMemberName("seoGeo")]
    SeoGeo,
    [JsonStringEnumMemberName("contentTypeRequirements")]
    ContentTypeRequirements,
}

public sealed class RagValidationIssueDto
{
    public string? SectionTitle { get; init; }
    public required RagValidationIssueCategory Category { get; init; }
    public required string Detail { get; init; }
    public required string RepairInstruction { get; init; }
}

public sealed class RagValidationDto
{
    public bool Approved { get; init; }
    public required IReadOnlyList<RagValidationIssueDto> Issues { get; init; }
    public required IReadOnlyList<string> Strengths { get; init; }
    public int UnsupportedClaimCount { get; init; }
    public double BriefAlignmentScore { get; init; }
    public double EvidenceCoverageScore { get; init; }
    public double UsefulnessScore { get; init; }
    public double OriginalityScore { get; init; }
    public double BrandAlignmentScore { get; init; }
}

public sealed class RagAdTemplateDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Channel { get; set; }
    public string? Framework { get; set; }
    public string Body { get; set; } = "";
}

public sealed class RagGenerateSourceDto
{
    public string Url { get; init; } = "";
    public string? Title { get; init; }
    public string? Entity { get; init; }
    public string? CrawlType { get; init; }
    /// <summary>page | theme | template — Phase D source kinds.</summary>
    public string? Kind { get; init; }
    public string? PageId { get; init; }
}

public sealed class RagCitationDto
{
    public string? PageId { get; init; }
    public string Url { get; init; } = "";
    public string? Title { get; init; }
    public string? SectionTitle { get; init; }
    public string Quote { get; init; } = "";
    public string? CrawlType { get; init; }
}

public sealed class RagThemeSourceDto
{
    public string Label { get; init; } = "";
    public string? Relationship { get; init; }
    public string? Entity { get; init; }
    public string? Url { get; init; }
}

public sealed class RagBattlecardDto
{
    public string PartnerSummary { get; set; } = "";
    public string CompetitorSummary { get; set; } = "";
    public List<string> Differentiators { get; set; } = [];
    public List<string> Risks { get; set; } = [];
}

public sealed class RagGenerateResponse
{
    public required string Intent { get; init; }
    public string? Content { get; init; }
    public IReadOnlyList<string>? Variations { get; init; }
    public RagBattlecardDto? Battlecard { get; init; }
    public IReadOnlyList<RagGenerateSourceDto> Sources { get; init; } = [];
    public IReadOnlyList<RagCitationDto>? Citations { get; init; }
    public IReadOnlyList<RagThemeSourceDto>? ThemeSources { get; init; }
    public IReadOnlyList<RagOutlineSectionDto>? Outline { get; init; }
    public IReadOnlyList<RagAdTemplateDto>? AppliedTemplates { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> EvidenceWarnings { get; init; } = [];
    public bool SoftDisabled { get; init; }
    public string? ModelUsed { get; init; }
    public string? RetrievalMode { get; init; }
    public string PromptVersion { get; init; } = "rag-generate/1";
    public RagGenerateProvenanceDto? Provenance { get; init; }
    public RagValidationDto? Validation { get; init; }
}

public sealed class RagGenerateStatusDto
{
    public bool Available { get; init; }
    public bool RagClientEnabled { get; init; }
    public bool GenerateEnabled { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<string> WritingIntents { get; init; } = RagWritingIntents.All;
    public IReadOnlyList<string> EntitySeeds { get; init; } = RagEntitySeedList.Names;
    public string LongFormModel { get; init; } = RagModelRouter.DefaultLongFormModel;
    public string ShortFormModel { get; init; } = RagModelRouter.DefaultStandardModel;
    public bool GraphRetrievalAvailable { get; init; }
    public bool AdTemplateIndexAvailable { get; init; }
    public bool CiteableGenerateAvailable { get; init; }
    public string ModelPolicyVersion { get; init; } = "content-model-policy.v1";
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ApprovedStageModels { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
    public string SkillCatalogVersion { get; init; } = GccV2SkillCatalog.CurrentVersion;
    public string SkillEnvelopeVersion { get; init; } = GccV2SkillExecutionSnapshot.CurrentEnvelopeVersion;
    public string SpecialistExecutorVersion { get; init; } = RagProducerCapabilities.RequiredSpecialistExecutorVersion;
    public IReadOnlyList<string> SpecialistExecutors { get; init; } = RagProducerCapabilities.RequiredSpecialists;
    public bool SpecialistToolsAllowed { get; init; }
}
