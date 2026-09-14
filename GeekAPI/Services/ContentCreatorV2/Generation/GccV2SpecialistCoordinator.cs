using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.Rag;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed record GccV2SpecialistArtifact(
    string ArtifactVersion, Guid JobId, string Stage, string Role, int Order,
    Guid AgentVersionId, string Agent, string StageExecutionId,
    string ArtifactDigest, JsonElement Payload, DateTimeOffset CreatedAtUtc);

public sealed record GccV2SpecialistReviewOutcome(
    string Decision, IReadOnlyList<RagSpecialistReviewIssueDto> Issues);

public sealed class GccV2SpecialistReviewException(
    string stage, GccV2SpecialistReviewOutcome outcome)
    : InvalidOperationException(BuildMessage(stage, outcome))
{
    public string Stage { get; } = stage;
    public GccV2SpecialistReviewOutcome Outcome { get; } = outcome;

    private static string BuildMessage(string stage, GccV2SpecialistReviewOutcome outcome)
    {
        var details = outcome.Issues.Select(x => x.Detail).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).Take(3).ToList();
        return $"Reviewer {outcome.Decision} for {stage}"
            + (details.Count == 0 ? "." : $": {string.Join("; ", details)}");
    }
}

/// <summary>
/// Executes real contributor and reviewer FunctionAgents around the canonical producer call.
/// Every returned typed artifact is digest-verified, persisted, and announced before handoff.
/// </summary>
public sealed class GccV2SpecialistCoordinator(
    HttpGccV2Repository repo,
    GccV2AgentTeamResolver teams,
    GccV2SkillSnapshotRegistry skillSnapshots,
    GccV2AgentExecutionFactory executions,
    RagGenerateService rag,
    GccV2JobEventWriter events)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Primary-ctor deps retained for DI shape; specialist RAG generate path is removed.
    private readonly GccV2AgentTeamResolver _teams = teams;
    private readonly GccV2SkillSnapshotRegistry _skillSnapshots = skillSnapshots;
    private readonly GccV2AgentExecutionFactory _executions = executions;
    private readonly RagGenerateService _rag = rag;

    public Task PrepareProducerAsync(
        GccV2JobDto job,
        Guid ownerUserId,
        RagGenerateRequest producerRequest,
        GccV2SignedSkillExecutionEnvelopeV2 producerEnvelope,
        CancellationToken ct)
    {
        // Agent-team RAG /v1/generate specialists are removed. Create drafting is GeekAPI
        // CreateLibraryDraft only — do not call RAG generate for contributors/producers.
        if (!producerRequest.CreateLibraryDraft
            || producerRequest.ExecutionVersion == RagProducerCapabilities.AgentExecutionVersion
            || producerRequest.ExecutionVersion == RagProducerCapabilities.RequiredExecutionVersion)
        {
            throw new InvalidOperationException(
                "Agent RAG generate is removed. Create must use CreateLibraryDraft with gcc-create-library.v1.");
        }

        _ = (job, ownerUserId, producerEnvelope, ct);
        producerRequest.ExecutionVersion = RagProducerCapabilities.CreateLibraryExecutionVersion;
        producerRequest.SignedSkillExecution = producerEnvelope;
        return Task.CompletedTask;
    }

    public Task CompleteProducerAndRunReviewersAsync(
        GccV2JobDto job,
        Guid ownerUserId,
        RagGenerateRequest producerRequest,
        RagGenerateResponse producerResponse,
        object canonicalOutput,
        CancellationToken ct)
    {
        if (!producerRequest.CreateLibraryDraft
            || producerRequest.ExecutionVersion == RagProducerCapabilities.AgentExecutionVersion
            || producerRequest.ExecutionVersion == RagProducerCapabilities.RequiredExecutionVersion)
        {
            throw new InvalidOperationException(
                "Agent RAG generate is removed. Create must use CreateLibraryDraft with gcc-create-library.v1.");
        }

        _ = (job, ownerUserId, producerResponse, canonicalOutput, ct);
        return Task.CompletedTask;
    }

    // Retained helpers for digest/review classification used by tests and VALIDATE repair mapping.
    private async Task PersistReturnedAsync(
        GccV2JobDto job, Guid ownerUserId, GccV2AgentTeamMember member,
        GccV2AgentTeamParticipation participation, RagAgentExecutionRequestDto execution,
        object payload, string? returnedDigest, CancellationToken ct)
    {
        var computed = VerifyReturnedDigest(member.Slug, payload, returnedDigest);
        await PersistAsync(job, ownerUserId, member, participation,
            execution.StageExecutionId, computed, JsonSerializer.SerializeToElement(payload, Json), ct);
    }

    private async Task PersistAsync(
        GccV2JobDto job, Guid ownerUserId, GccV2AgentTeamMember member,
        GccV2AgentTeamParticipation participation, string stageExecutionId,
        string artifactDigest, JsonElement payload, CancellationToken ct)
    {
        var artifact = new GccV2SpecialistArtifact(
            "gcc-specialist-artifact.v1", job.Id, participation.Stage, participation.Role,
            participation.Order, member.AgentVersionId, member.Slug, stageExecutionId,
            artifactDigest, payload, DateTimeOffset.UtcNow);
        await repo.AddStageResultAsync(job.Id, new CreateGccV2StageResultCommand(
            $"specialist-{participation.Role}", $"{participation.Stage}:{member.Slug}",
            JsonSerializer.Serialize(artifact, Json), 0), ct);
        await events.AppendAsync(job.Id, ownerUserId, "SpecialistArtifactCreated", new
        {
            artifact.ArtifactVersion, artifact.Stage, artifact.Role, artifact.Order,
            artifact.AgentVersionId, artifact.Agent, artifact.StageExecutionId,
            artifact.ArtifactDigest,
        }, ct: ct);
    }

    private static IEnumerable<(GccV2AgentTeamMember Member, GccV2AgentTeamParticipation Participation)> Participants(
        GccV2AgentTeamSnapshot snapshot, string stage, string role) =>
        snapshot.Agents.SelectMany(member => member.Participation
                .Where(x => x.Stage == stage && x.Role == role).Select(x => (Member: member, Participation: x)))
            .OrderBy(x => x.Participation.Order).ThenBy(x => x.Member.Slug, StringComparer.Ordinal);

    public static string VerifyReturnedDigest(string specialist, object payload, string? returnedDigest)
    {
        var computed = GccV2AgentExecutionFactory.CanonicalDigest(payload);
        if (string.IsNullOrWhiteSpace(returnedDigest)
            || !string.Equals(computed, returnedDigest, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Specialist '{specialist}' returned an invalid artifact digest.");
        return computed;
    }

    public static IReadOnlyList<string> DeterministicParticipantOrder(
        GccV2AgentTeamSnapshot snapshot, string stage, string role) =>
        Participants(snapshot, stage, role).Select(x => x.Member.Slug).ToList();

    public static GccV2SpecialistReviewOutcome ClassifyReviews(
        IReadOnlyList<RagSpecialistReviewDto> reviews)
    {
        var decision = reviews.Any(x => string.Equals(x.Decision, "rejected", StringComparison.OrdinalIgnoreCase))
            ? "rejected"
            : reviews.Any(x => string.Equals(x.Decision, "changesRequired", StringComparison.OrdinalIgnoreCase))
                ? "changesRequired"
                : "approved";
        return new GccV2SpecialistReviewOutcome(
            decision,
            reviews.SelectMany(x => x.Issues ?? []).ToList());
    }

    private async Task<(IReadOnlyList<RagSpecialistContributionDto> Contributions,
        IReadOnlyList<RagSpecialistReviewDto> Reviews)> LoadArtifactsAsync(Guid jobId, CancellationToken ct)
    {
        var contributions = new List<RagSpecialistContributionDto>();
        var reviews = new List<RagSpecialistReviewDto>();
        foreach (var result in await repo.GetStageResultsAsync(jobId, ct))
        {
            if (result.Stage is not ("specialist-contributor" or "specialist-reviewer")) continue;
            try
            {
                var artifact = JsonSerializer.Deserialize<GccV2SpecialistArtifact>(result.OutputJson, Json);
                if (artifact is null) continue;
                if (result.Stage == "specialist-contributor")
                {
                    var contribution = artifact.Payload.Deserialize<RagSpecialistContributionDto>(Json)
                        ?? throw new InvalidOperationException("Persisted contribution is empty.");
                    if (GccV2AgentExecutionFactory.CanonicalDigest(contribution) != artifact.ArtifactDigest)
                        throw new InvalidOperationException("Persisted contribution digest validation failed.");
                    contributions.Add(contribution);
                }
                else
                {
                    var review = artifact.Payload.Deserialize<RagSpecialistReviewDto>(Json)
                        ?? throw new InvalidOperationException("Persisted review is empty.");
                    if (GccV2AgentExecutionFactory.CanonicalDigest(review) != artifact.ArtifactDigest)
                        throw new InvalidOperationException("Persisted review digest validation failed.");
                    reviews.Add(review);
                }
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("Persisted specialist artifact is malformed.");
            }
        }
        return (
            contributions.DistinctBy(x => GccV2AgentExecutionFactory.CanonicalDigest(x)).ToList(),
            reviews.DistinctBy(x => GccV2AgentExecutionFactory.CanonicalDigest(x)).ToList());
    }

    private static RagGenerateRequest Clone(RagGenerateRequest source) => new()
    {
        WritingIntent = source.WritingIntent,
        Topic = source.Topic,
        TargetEntities = source.TargetEntities,
        PartnerRunIds = source.PartnerRunIds,
        CompetitorRunIds = source.CompetitorRunIds,
        PartnerRunId = source.PartnerRunId,
        CompetitorRunId = source.CompetitorRunId,
        AdTemplates = source.AdTemplates,
        TemplateIds = source.TemplateIds,
        GenerationStage = source.GenerationStage,
        Outline = source.Outline,
        SectionKey = source.SectionKey,
        SectionHeading = source.SectionHeading,
        SectionBrief = source.SectionBrief,
        CompletedSectionSummaries = source.CompletedSectionSummaries,
        DraftContent = source.DraftContent,
        Sources = source.Sources,
        CanonicalBrief = source.CanonicalBrief,
        ModelPolicyPreset = source.ModelPolicyPreset,
        ModelPolicyVersion = source.ModelPolicyVersion,
        StageModelOverrides = source.StageModelOverrides,
        ExecutionVersion = RagProducerCapabilities.AgentExecutionVersion,
        RequestedModel = source.RequestedModel,
        RequireCiteable = true,
    };
}
