using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.Rag;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed record GccV2SpecialistArtifact(
    string ArtifactVersion, Guid JobId, string Stage, string Role, int Order,
    Guid AgentVersionId, string Agent, string StageExecutionId,
    string ArtifactDigest, JsonElement Payload, DateTimeOffset CreatedAtUtc);

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

    public async Task PrepareProducerAsync(
        GccV2JobDto job,
        Guid ownerUserId,
        RagGenerateRequest producerRequest,
        GccV2SignedSkillExecutionEnvelopeV2 producerEnvelope,
        CancellationToken ct)
    {
        var snapshot = teams.ValidatePersisted(job);
        var stage = producerRequest.GenerationStage;
        var coordinatorExecutionId = Guid.NewGuid().ToString("D");
        var persisted = await LoadArtifactsAsync(job.Id, ct);
        var contributions = persisted.Contributions.ToList();
        var reviews = persisted.Reviews.ToList();
        foreach (var item in Participants(snapshot, stage, "contributor"))
        {
            var attemptId = Guid.NewGuid().ToString("D");
            var envelope = await skillSnapshots.BuildEnvelopeAsync(job, attemptId, stage, ct);
            var request = Clone(producerRequest);
            request.ExecutionVersion = RagProducerCapabilities.AgentExecutionVersion;
            request.JobId = job.Id.ToString("D");
            request.AttemptId = attemptId;
            request.SignedSkillExecution = envelope;
            request.SpecialistContributions = contributions;
            request.SpecialistReviews = reviews;
            request.AgentExecution = await executions.CreateForMemberAsync(
                job, attemptId, stage, "contributor", coordinatorExecutionId,
                item.Member, envelope, request, ct);
            var response = await rag.GenerateAsync(job.OwnerUserId, request, ct)
                ?? throw new InvalidOperationException($"Contributor '{item.Member.Slug}' returned no response.");
            var contribution = response.SpecialistContribution
                ?? throw new InvalidOperationException($"Contributor '{item.Member.Slug}' omitted specialistContribution.");
            await PersistReturnedAsync(job, ownerUserId, item.Member, item.Participation,
                request.AgentExecution, contribution, response.SpecialistArtifactDigest, ct);
            if (!contributions.Any(x =>
                GccV2AgentExecutionFactory.CanonicalDigest(x) == response.SpecialistArtifactDigest))
                contributions.Add(contribution);
        }

        producerRequest.ExecutionVersion = RagProducerCapabilities.AgentExecutionVersion;
        producerRequest.JobId = job.Id.ToString("D");
        producerRequest.SignedSkillExecution = producerEnvelope;
        producerRequest.SpecialistContributions = contributions;
        producerRequest.SpecialistReviews = reviews;
        producerRequest.AgentExecution = await executions.CreateAsync(
            job, producerRequest.AttemptId, stage, "producer", coordinatorExecutionId,
            producerEnvelope, producerRequest, ct);
    }

    public async Task CompleteProducerAndRunReviewersAsync(
        GccV2JobDto job,
        Guid ownerUserId,
        RagGenerateRequest producerRequest,
        RagGenerateResponse producerResponse,
        object canonicalOutput,
        CancellationToken ct)
    {
        var snapshot = teams.ValidatePersisted(job);
        var stage = producerRequest.GenerationStage;
        var producers = Participants(snapshot, stage, "producer").ToList();
        if (producers.Count != 1)
            throw new InvalidOperationException($"The team must have exactly one producer for {stage}.");
        var producerExecution = producerResponse.AgentExecution
            ?? throw new InvalidOperationException("Producer response omitted agentExecution provenance.");
        var canonicalElement = JsonSerializer.SerializeToElement(canonicalOutput, Json);
        var canonicalDigest = GccV2AgentExecutionFactory.CanonicalDigest(canonicalOutput);
        await PersistAsync(job, ownerUserId, producers[0].Member, producers[0].Participation,
            producerExecution.StageExecutionId, canonicalDigest, canonicalElement, ct);

        var coordinatorExecutionId = producerExecution.CoordinatorExecutionId;
        var contributions = producerRequest.SpecialistContributions?.ToList() ?? [];
        var reviews = producerRequest.SpecialistReviews?.ToList() ?? [];
        foreach (var item in Participants(snapshot, stage, "reviewer"))
        {
            var attemptId = Guid.NewGuid().ToString("D");
            var envelope = await skillSnapshots.BuildEnvelopeAsync(job, attemptId, stage, ct);
            var request = Clone(producerRequest);
            request.AttemptId = attemptId;
            request.JobId = job.Id.ToString("D");
            request.SignedSkillExecution = envelope;
            request.SpecialistContributions = contributions;
            request.SpecialistReviews = reviews;
            request.AgentExecution = await executions.CreateForMemberAsync(
                job, attemptId, stage, "reviewer", coordinatorExecutionId,
                item.Member, envelope, request, ct);
            var response = await rag.GenerateAsync(job.OwnerUserId, request, ct)
                ?? throw new InvalidOperationException($"Reviewer '{item.Member.Slug}' returned no response.");
            var review = response.SpecialistReview
                ?? throw new InvalidOperationException($"Reviewer '{item.Member.Slug}' omitted specialistReview.");
            await PersistReturnedAsync(job, ownerUserId, item.Member, item.Participation,
                request.AgentExecution, review, response.SpecialistArtifactDigest, ct);
            if (!reviews.Any(x =>
                GccV2AgentExecutionFactory.CanonicalDigest(x) == response.SpecialistArtifactDigest))
                reviews.Add(review);
        }
    }

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
