using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Rag;

namespace GeekAPI.Services.ContentCreatorV2.AgentTests;

public sealed record GccV2AgentSmokeResult(
    bool Attempted, bool Passed, string? Reason, string? StageExecutionId);

public sealed class GccV2AgentRagSmokeExecutor(
    HttpGccV2Repository repo,
    IGeekCrawlerRagClient rag,
    GccV2SkillSnapshotSigner skillSigner,
    GccV2AgentTeamSigner agentSigner)
{
    public async Task<GccV2AgentSmokeResult> ExecuteAsync(
        GccV2AgentTestRunDto run, GccV2AgentDto agent, GccV2AgentVersionDto version,
        bool required, CancellationToken ct)
    {
        if (!rag.IsEnabled || !skillSigner.IsConfigured || !agentSigner.IsConfigured)
            return new(false, !required, "RAG or execution signing is not configured.", null);
        var capabilities = await rag.GetCapabilitiesAsync(ct);
        if (capabilities is null
            || !capabilities.ExecutionVersions.Contains(RagProducerCapabilities.AgentExecutionVersion)
            || !capabilities.SkillEnvelopeVersions.Contains(GccV2SignedSkillExecutionEnvelopeV2.CurrentEnvelopeVersion)
            || !capabilities.ToolsAllowed)
            return new(false, !required, "RAG does not advertise the strict v3 specialist protocol.", null);

        var participation = version.StageParticipation
            .OrderBy(x => x.Stage == "researchPlanning" ? 0 : 1)
            .ThenBy(x => x.Order).First();
        var stage = participation.Stage;
        if (!capabilities.GenerationStages.Contains(stage))
            return new(false, !required, $"RAG does not support test stage '{stage}'.", null);
        var contentType = (JsonSerializer.Deserialize<List<string>>(version.ContentTypesJson) ?? []).First();
        var attemptId = Guid.NewGuid().ToString("D");
        var resolvedAt = new DateTimeOffset(
            DateTimeOffset.UtcNow.Ticks - DateTimeOffset.UtcNow.Ticks % TimeSpan.TicksPerSecond,
            TimeSpan.Zero);
        var skills = new List<GccV2SkillSnapshotEntryV2>();
        foreach (var assignment in version.Skills.OrderBy(x => x.Order))
        {
            var package = await repo.GetSkillAsync(assignment.SkillVersion.Package.Id, ct)
                ?? throw new InvalidOperationException("Assigned test skill package was not found.");
            var pinned = package.Versions.Single(x => x.Id == assignment.SkillVersionId);
            var skillMd = pinned.Files.SingleOrDefault(x => x.RelativePath == "SKILL.md")
                ?? await repo.GetSkillFileAsync(pinned.Id, "SKILL.md", ct)
                ?? throw new InvalidOperationException($"Skill '{package.Slug}' has no SKILL.md.");
            var applicable = pinned.Applicability.Where(x =>
                x.Stage == stage && x.ContentType == contentType).ToList();
            skills.Add(new(
                package.Slug, package.DisplayName, package.Description, pinned.SemanticVersion,
                $"{package.Slug}:{pinned.SemanticVersion}:{pinned.PackageSha256[..16]}",
                pinned.PackageSha256, skillMd.Sha256, skillMd.Content,
                package.SourceRepository, pinned.ImmutableGitRef, pinned.State,
                applicable.Select(x => x.Stage).Distinct().ToList(),
                applicable.Select(x => x.ContentType).Distinct().ToList(),
                applicable.SelectMany(x => JsonSerializer.Deserialize<List<string>>(x.RequiredToolsJson) ?? [])
                    .Distinct().Order().ToList(),
                assignment.Order, [], []));
        }
        var unsignedEnvelope = new GccV2SignedSkillExecutionEnvelopeV2(
            GccV2SignedSkillExecutionEnvelopeV2.CurrentEnvelopeVersion,
            GccV2SkillSnapshotRegistry.CatalogVersion, new string('0', 64), new string('0', 64),
            skillSigner.KeyId, contentType, run.Id.ToString("D"), attemptId, resolvedAt, skills);
        var envelopeDigest = GccV2SkillSnapshotRegistry.ComputeSnapshotDigest(unsignedEnvelope);
        var envelope = unsignedEnvelope with
        {
            SnapshotDigest = envelopeDigest,
            Signature = skillSigner.SignDigest(envelopeDigest),
        };

        var executionInstructions =
            $"Objective:\n{version.Objective}\n\nInstructions:\n{version.Instructions}";
        var instructionsDigest = Hash(executionInstructions);
        var policy = JsonSerializer.Serialize(new
        {
            objective = version.Objective,
            modelPolicyVersion = version.ModelPolicyVersion,
            modelPolicyProfile = version.ModelPolicyProfile,
            contentTypes = JsonSerializer.Deserialize<List<string>>(version.ContentTypesJson) ?? [],
            tools = JsonSerializer.Deserialize<List<string>>(version.AllowedToolsJson) ?? [],
            models = JsonSerializer.Deserialize<List<string>>(version.AllowedModelsJson) ?? [],
            skillVersionIds = version.Skills.OrderBy(x => x.Order).Select(x => x.SkillVersionId),
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var models = (JsonSerializer.Deserialize<List<string>>(version.AllowedModelsJson) ?? [])
            .Where(x => ContentModelPolicy.IsApproved(stage, x)).Order().ToList();
        var tools = StageTools(stage, participation.Role);
        var unsignedSelected = new RagSelectedAgentDto(
            agent.Slug, version.SemanticVersion, new string('0', 64), agent.DisplayName,
            participation.Role, executionInstructions, instructionsDigest, policy, Hash(policy),
            version.StageParticipation.Where(x => x.Role == participation.Role)
                .Select(x => x.Stage).Distinct().Order().ToList(),
            tools, models);
        var selected = unsignedSelected with
        {
            Digest = GccV2AgentExecutionFactory.CanonicalDigest(unsignedSelected, "digest"),
        };
        var now = new DateTimeOffset(
            DateTimeOffset.UtcNow.Ticks - DateTimeOffset.UtcNow.Ticks % 10, TimeSpan.Zero);
        var stageExecutionId = Guid.NewGuid().ToString("D");
        var assigned = skills.Select(x => new RagAgentSkillReferenceDto(
            x.Id, x.Version, x.ActivationId, x.PackageDigest, "required")).ToList();
        var unsignedExecution = new RagAgentExecutionRequestDto(
            "specialist-team-execution.v1", new string('0', 64), new string('0', 64),
            agentSigner.KeyId, run.Id.ToString("D"), attemptId, run.Id.ToString("D"),
            stageExecutionId, Hash($"{run.Id:D}|{stageExecutionId}"), now, now.AddMinutes(10),
            1, null, stage, selected, participation.Role switch
            {
                "contributor" => "contributorOutput.v1",
                "reviewer" => "reviewerOutput.v1",
                _ => "producerOutput.v1",
            }, assigned, [], new RagAgentBudgetDto(MaxTurns: 4, MaxToolCalls: 8));
        var executionDigest = GccV2AgentExecutionFactory.CanonicalDigest(
            unsignedExecution, "snapshotDigest", "signature", "cancelled");
        var execution = unsignedExecution with
        {
            SnapshotDigest = executionDigest,
            Signature = agentSigner.Sign(executionDigest),
        };
        var response = await rag.GenerateAsync(new GeekCrawlerRagGenerateRequest
        {
            WritingIntent = "Technical Article",
            Topic = $"Governed specialist test for {agent.DisplayName}",
            GenerationStage = stage,
            ExecutionVersion = RagProducerCapabilities.AgentExecutionVersion,
            JobId = run.Id.ToString("D"),
            AttemptId = attemptId,
            SignedSkillExecution = envelope,
            AgentExecution = execution,
        }, ct);
        if (response?.AgentFailure is { } failure)
            return new(true, false, $"{failure.ErrorClass}: {failure.Detail}", stageExecutionId);
        var completed = response?.AgentExecution?.StopReason == RagAgentStopReason.Completed;
        var typed = participation.Role switch
        {
            "contributor" => response?.SpecialistContribution is not null,
            "reviewer" => response?.SpecialistReview is not null,
            _ => completed,
        };
        return new(true, completed && typed,
            completed && typed ? null : "RAG smoke response was incomplete or not completed.",
            stageExecutionId);
    }

    private static IReadOnlyList<string> StageTools(string stage, string role)
    {
        var read = stage switch
        {
            "researchPlanning" or "outline" =>
                new[] { "search_corpus", "load_evidence_page", "get_brief_context",
                    "get_specialist_artifacts", "activate_skill", "read_skill_resource" },
            "section" or "repair" =>
                ["load_evidence_page", "get_brief_context", "get_outline_context",
                    "get_completed_section_summaries", "activate_skill", "read_skill_resource",
                    "get_specialist_artifacts"],
            _ => ["load_evidence_page", "get_brief_context", "get_outline_context",
                "get_specialist_artifacts", "activate_skill", "read_skill_resource"],
        };
        var terminal = role switch
        {
            "contributor" => "submit_contribution",
            "reviewer" => "submit_review",
            _ => stage switch
            {
                "researchPlanning" => "submit_research_plan", "outline" => "submit_outline",
                "section" => "submit_section", "repair" => "submit_repair",
                "validation" => "submit_validation", _ => "submit_final_synthesis",
            },
        };
        return read.Append(terminal).Distinct().Order().ToList();
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
