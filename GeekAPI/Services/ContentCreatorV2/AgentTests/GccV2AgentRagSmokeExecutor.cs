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
        if (!capabilities.ExecutionVersions.Contains(RagProducerCapabilities.AgentExecutionVersion)
            || !capabilities.SkillEnvelopeVersions.Contains(GccV2SignedSkillExecutionEnvelopeV2.CurrentEnvelopeVersion)
            || !capabilities.ToolsAllowed)
            return new(false, !required, "RAG does not advertise the strict v3 specialist protocol.", null);

        var participation = version.StageParticipation
            .OrderBy(x => x.Stage == "researchPlanning" ? 0 : 1)
            .ThenBy(x => x.Order).First();
        var stage = participation.Stage;
        if (!capabilities.GenerationStages.Contains(stage))
            return new(false, !required, $"RAG does not support test stage '{stage}'.", null);

        throw new InvalidOperationException(
            "RAG generate (/v1/generate) is removed. Agent smoke tests cannot call Geek-Crawler-Rag generate.");
    }
}
