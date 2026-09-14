using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;

namespace GeekAPI.Services.ContentCreatorV2.AgentTests;

public sealed record GccV2AgentSmokeResult(
    bool Attempted, bool Passed, string? Reason, string? StageExecutionId);

/// <summary>
/// Agent-team smoke against RAG <c>/v1/generate</c> is removed.
/// Create drafting is GeekAPI library draft only.
/// </summary>
public sealed class GccV2AgentRagSmokeExecutor(IGeekCrawlerRagClient rag)
{
    public Task<GccV2AgentSmokeResult> ExecuteAsync(
        GccV2AgentTestRunDto run, GccV2AgentDto agent, GccV2AgentVersionDto version,
        bool required, CancellationToken ct)
    {
        _ = (run, agent, version, ct, rag);
        return Task.FromResult(new GccV2AgentSmokeResult(
            Attempted: false,
            Passed: !required,
            Reason: "RAG generate (/v1/generate) is removed. Agent smoke cannot call Geek-Crawler-Rag generate.",
            StageExecutionId: null));
    }
}
