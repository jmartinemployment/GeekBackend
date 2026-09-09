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
    public string? JobId { get; set; }
    public string AttemptId { get; set; } = Guid.NewGuid().ToString("D");
    public RagContextManifestEnvelopeDto? ContextManifest { get; set; }
    public IReadOnlyList<RagGovernedContextEntryDto>? GovernedContext { get; set; }
    [JsonIgnore]
    public GccV2SkillExecutionSnapshot? SkillExecution { get; set; }
    [JsonIgnore]
    public GccV2SignedSkillExecutionEnvelopeV2? SignedSkillExecution { get; set; }
    [JsonIgnore]
    public RagAgentExecutionRequestDto? AgentExecution { get; set; }
    public IReadOnlyList<RagSpecialistContributionDto>? SpecialistContributions { get; set; }
    public IReadOnlyList<RagSpecialistReviewDto>? SpecialistReviews { get; set; }
    /// <summary>Optional precomputed research queries handed from PLAN researchPlanning → outline.</summary>
    public IReadOnlyList<RagResearchQueryPlanDto>? ResearchPlan { get; set; }
    /// <summary>Expected effective model, used locally to reject producer substitution.</summary>
    public string? RequestedModel { get; set; }
    /// <summary>Canonical jobs require quote-verified RAG and may not use the local one-shot writer.</summary>
    public bool RequireCiteable { get; set; }
}

public sealed record RagContextManifestEnvelopeDto(
    string CanonicalJson, string Sha256, string Signature, string SigningKeyId);
public sealed record RagGovernedContextEntryDto(
    string Kind, Guid StableId, Guid VersionId, int VersionNumber, string Digest,
    JsonElement Payload, IReadOnlyList<Guid>? SelectedFieldIds = null,
    IReadOnlyList<string>? ApprovedClaims = null,
    IReadOnlyList<string>? ProhibitedClaims = null,
    IReadOnlyList<string>? MandatoryDisclaimers = null);

public sealed class RagOutlineSectionDto
{
    public string Key { get; set; } = "";
    public string Heading { get; set; } = "";
    public string Brief { get; set; } = "";
    public IReadOnlyList<string> EvidenceIds { get; set; } = [];
}

public sealed record RagResearchQueryPlanDto(string RunId, string CrawlType, string Need);

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
    public RagAgentExecutionProvenanceDto? AgentExecution { get; init; }
}

public static class RagProducerCapabilities
{
    public const string RequiredExecutionVersion = "rag-generate.v2";
    public const string AgentExecutionVersion = "rag-generate.v3";
    public const string RequiredSkillEnvelopeVersion = GccV2SkillExecutionSnapshot.CurrentEnvelopeVersion;
    public const string AgentSkillEnvelopeVersion = GccV2SignedSkillExecutionEnvelopeV2.CurrentEnvelopeVersion;
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

public sealed record RagAgentBudgetDto(
    int MaxTurns = 8, int MaxToolCalls = 24, int MaxRetrievedPages = 8,
    int MaxActiveSkills = 6, long MaxSkillBytes = 32768, long MaxResourceBytes = 32768,
    int MaxOutputTokens = 16000, double MaxStageSeconds = 300, int MaxRepairAttempts = 2);
public sealed record RagAgentSkillReferenceDto(
    string SkillId, string Version, string ActivationId, string PackageDigest, string Assignment);
public sealed record RagArtifactInputReferenceDto(string ArtifactId, string ArtifactType, string Digest);
public sealed record RagSelectedAgentDto(
    string Id, string Version, string Digest, string Name, string Role,
    string Instructions, string InstructionsDigest, string Policy, string PolicyDigest,
    IReadOnlyList<string> Stages, IReadOnlyList<string> ToolIds, IReadOnlyList<string> ModelIds);
public sealed record RagAgentExecutionRequestDto(
    string ContractVersion, string SnapshotDigest, string Signature, string SignatureKeyId,
    string JobId, string AttemptId, string CoordinatorExecutionId, string StageExecutionId,
    string IdempotencyKey, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc,
    int AttemptNumber,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? RetryOfStageExecutionId,
    string Stage,
    RagSelectedAgentDto SelectedAgent, string OutputContract,
    IReadOnlyList<RagAgentSkillReferenceDto> AssignedSkills,
    IReadOnlyList<RagArtifactInputReferenceDto> ArtifactInputs,
    RagAgentBudgetDto Limits, bool Cancelled = false, int RepairAttempt = 0);
public sealed record RagAgentBudgetUsageDto(
    int Turns, int ToolCalls, int RetrievedPages, int ActiveSkills,
    long SkillBytes, long ResourceBytes, int OutputTokens);
public sealed record RagAgentToolTraceDto(
    int Sequence, string ToolId, string ToolVersion, string Classification,
    string ArgumentDigest, string? ResultDigest, int? ResultCount, long DurationMs,
    string? ErrorClass, RagAgentBudgetUsageDto BudgetAfter);
public sealed record RagActivatedSkillDto(
    string SkillId, string Version, string ActivationId, string PackageDigest,
    IReadOnlyList<string> ResourcePaths);
public sealed record RagAgentExecutionProvenanceDto(
    string TraceVersion, string ToolsVersion, string WorkflowVersion, string ExecutorVersion,
    string Stage, string Agent, string JobId, string CoordinatorExecutionId,
    string StageExecutionId, string IdempotencyKey, string SelectedAgentId,
    string SelectedAgentVersion, string SelectedAgentDigest, string Role,
    string OutputContract, string PromptVersion, RagAgentStopReason StopReason,
    RagAgentBudgetDto Limits, RagAgentBudgetUsageDto Usage,
    IReadOnlyList<RagAgentToolTraceDto> ToolCalls,
    IReadOnlyList<RagActivatedSkillDto> ActivatedSkills,
    IReadOnlyList<RagAgentSkillReferenceDto> AssignedSkills,
    IReadOnlyList<RagArtifactInputReferenceDto> ArtifactInputs,
    string? RetryOfStageExecutionId, int AttemptNumber);
public sealed record RagAgentFailureDto(
    RagAgentStopReason StopReason, string ErrorClass, string Detail, bool Retryable);

[JsonConverter(typeof(JsonStringEnumConverter<RagAgentStopReason>))]
public enum RagAgentStopReason
{
    [JsonStringEnumMemberName("completed")] Completed,
    [JsonStringEnumMemberName("budgetExhausted")] BudgetExhausted,
    [JsonStringEnumMemberName("skillActivationFailed")] SkillActivationFailed,
    [JsonStringEnumMemberName("incompatibleProtocol")] IncompatibleProtocol,
    [JsonStringEnumMemberName("invalidStructuredOutput")] InvalidStructuredOutput,
    [JsonStringEnumMemberName("toolDenied")] ToolDenied,
    [JsonStringEnumMemberName("evidenceFailure")] EvidenceFailure,
    [JsonStringEnumMemberName("reviewerChangesRequired")] ReviewerChangesRequired,
    [JsonStringEnumMemberName("reviewerRejected")] ReviewerRejected,
    [JsonStringEnumMemberName("upstreamFailure")] UpstreamFailure,
    [JsonStringEnumMemberName("cancelled")] Cancelled,
    [JsonStringEnumMemberName("timedOut")] TimedOut,
}

public sealed class RagAgentStoppedException(
    RagAgentStopReason reason,
    string? detail,
    IReadOnlyList<RagSpecialistReviewIssueDto>? reviewIssues = null)
    : Exception(detail ?? $"RAG agent stopped: {reason}.")
{
    public RagAgentStopReason Reason { get; } = reason;
    public string? Detail { get; } = detail;
    public IReadOnlyList<RagSpecialistReviewIssueDto> ReviewIssues { get; } = reviewIssues ?? [];
    /// <summary>
    /// Only upstream failures are safe to blind-retry. Reviewer <c>changesRequired</c> must
    /// feed the VALIDATE→REPAIR loop (or fail closed) — never requeue the whole job without
    /// applying the reviewer's issues.
    /// </summary>
    public bool IsTransient => Reason is RagAgentStopReason.UpstreamFailure;
}

public sealed record RagContributionQueryDto(string Need, string Corpus);
public sealed record RagContributionOutlineSectionDto(
    string Key, string Heading, string Brief, IReadOnlyList<string> EvidenceIds);
public sealed record RagSpecialistContributionDto(
    string ContractVersion, string Stage, string Summary, string? ProposedContent,
    IReadOnlyList<RagContributionOutlineSectionDto>? ProposedOutline,
    IReadOnlyList<RagContributionQueryDto>? ProposedQueries,
    IReadOnlyList<string>? Recommendations, IReadOnlyList<RagCitationDto> Citations);
public sealed record RagSpecialistReviewIssueDto(
    string Category, string Severity, string? SectionTitle, string Detail,
    string Recommendation, RagCitationDto? Citation);
public sealed record RagSpecialistReviewDto(
    string ContractVersion, string Stage, string Decision, string Summary,
    IReadOnlyList<RagSpecialistReviewIssueDto> Issues, IReadOnlyList<RagCitationDto> Citations);

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
    public IReadOnlyList<RagResearchQueryPlanDto>? ResearchPlan { get; init; }
    public IReadOnlyList<RagAdTemplateDto>? AppliedTemplates { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> EvidenceWarnings { get; init; } = [];
    public bool SoftDisabled { get; init; }
    public string? ModelUsed { get; init; }
    public string? RetrievalMode { get; init; }
    public string PromptVersion { get; init; } = "rag-generate/1";
    public RagGenerateProvenanceDto? Provenance { get; init; }
    public RagValidationDto? Validation { get; init; }
    public RagAgentExecutionProvenanceDto? AgentExecution { get; init; }
    public RagAgentFailureDto? AgentFailure { get; init; }
    public RagSpecialistContributionDto? SpecialistContribution { get; init; }
    public RagSpecialistReviewDto? SpecialistReview { get; init; }
    public string? SpecialistArtifactDigest { get; init; }
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
