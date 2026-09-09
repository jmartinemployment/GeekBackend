using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.AgentTests;

public sealed class GccV2AgentTestWorker(
    GccV2AgentTestWake wake,
    IServiceScopeFactory scopes,
    ILogger<GccV2AgentTestWorker> logger) : BackgroundService
{
    private readonly string _instanceId = $"agent-test-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverOnceAsync(stoppingToken);
        try
        {
            while (await wake.Reader.WaitToReadAsync(stoppingToken))
                while (wake.Reader.TryRead(out var runId))
                    try { await ProcessAsync(runId, stoppingToken); }
                    catch (Exception ex) { logger.LogError(ex, "Agent test run {RunId} failed.", runId); }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task RecoverOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
            foreach (var run in await repo.GetAgentTestRunsByStatusAsync("queued", limit: 200, ct: ct))
                wake.Wake(run.Id);
            foreach (var run in await repo.GetAgentTestRunsByStatusAsync(
                "running", DateTimeOffset.UtcNow, 200, ct))
                wake.Wake(run.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent test startup recovery scan failed.");
        }
    }

    private async Task ProcessAsync(Guid runId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        var notifier = scope.ServiceProvider.GetRequiredService<GccV2AgentTestProgressNotifier>();
        GccV2AgentTestRunDto run;
        try { run = await repo.ClaimAgentTestRunAsync(runId, _instanceId, ct: ct); }
        catch (HttpRequestException) { return; }
        await notifier.PushAsync(run, "Test run claimed.", ct);
        try
        {
            if (run.CancellationRequestedAtUtc is not null)
            {
                await CompleteAsync(repo, notifier, run, "cancelled",
                    new { contractVersion = "gcc-agent-test-result.v1", cancelled = true },
                    "Cancellation requested.", ct);
                return;
            }
            run = await ProgressAsync(repo, notifier, run, 15, "contract-validation",
                "Validating immutable agent contract.", ct);
            var agents = await repo.ListAgentsAsync(ct: ct);
            var pair = agents.SelectMany(a => a.Versions.Select(v => (Agent: a, Version: v)))
                .SingleOrDefault(x => x.Version.Id == run.AgentVersionId);
            if (pair.Version is null)
                throw new InvalidOperationException("Agent version no longer exists.");
            if (!string.Equals(pair.Version.VersionDigest, run.VersionDigest, StringComparison.Ordinal))
                throw new InvalidOperationException("Agent version digest changed after the run was queued.");
            var failures = GccV2AgentTestPolicy.Failures(pair.Version);
            if (failures.Count > 0)
            {
                await CompleteAsync(repo, notifier, run, "failed", new
                {
                    contractVersion = "gcc-agent-test-result.v1",
                    contractPassed = false,
                    failures,
                    realExecution = new { attempted = false, required = RequiresReal(run), passed = false },
                }, "Agent contract validation failed.", ct);
                return;
            }

            run = await ProgressAsync(repo, notifier, run, 55, "rag-smoke",
                "Evaluating representative RAG smoke execution.", ct);
            var smoke = await scope.ServiceProvider.GetRequiredService<GccV2AgentRagSmokeExecutor>()
                .ExecuteAsync(run, pair.Agent, pair.Version, RequiresReal(run), ct);
            var current = await repo.GetAgentTestRunAsync(run.Id, ct);
            if (current?.CancellationRequestedAtUtc is not null)
            {
                await CompleteAsync(repo, notifier, current, "cancelled",
                    new { contractVersion = "gcc-agent-test-result.v1", cancelled = true },
                    "Cancellation requested.", ct);
                return;
            }
            if (!smoke.Passed)
            {
                await CompleteAsync(repo, notifier, run, "failed", new
                {
                    contractVersion = "gcc-agent-test-result.v1",
                    contractPassed = true,
                    failures = Array.Empty<string>(),
                    realExecution = smoke,
                }, smoke.Reason ?? "RAG smoke execution failed.", ct);
                return;
            }
            await CompleteAsync(repo, notifier, run, "passed", new
            {
                contractVersion = "gcc-agent-test-result.v1",
                contractPassed = true,
                failures = Array.Empty<string>(),
                realExecution = smoke,
            }, null, ct);
            if (run.Scenario == "first-party-seed")
            {
                try
                {
                    await repo.TransitionAgentVersionAsync(run.AgentVersionId, "publish",
                        new("system:first-party-seed", null, null, $"agent-test:{run.Id:D}"), ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Passing first-party test {RunId} could not publish version.", run.Id);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await CompleteAsync(repo, notifier, run, "failed", new
            {
                contractVersion = "gcc-agent-test-result.v1",
                contractPassed = false,
            }, ex.Message, ct);
        }
    }

    private static bool RequiresReal(GccV2AgentTestRunDto run)
    {
        if (run.Scenario == "rag-smoke") return true;
        try
        {
            using var input = JsonDocument.Parse(run.InputJson);
            return input.RootElement.TryGetProperty("realExecutionRequired", out var required)
                && required.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return true; }
    }

    private static async Task<GccV2AgentTestRunDto> ProgressAsync(
        HttpGccV2Repository repo, GccV2AgentTestProgressNotifier notifier,
        GccV2AgentTestRunDto run, int progress, string phase, string message, CancellationToken ct)
    {
        var updated = await repo.PatchAgentTestRunAsync(run.Id, new(
            null, progress, phase, null, null, DateTimeOffset.UtcNow.AddMinutes(2), null,
            "system:agent-test-worker", null, $"agent-test:{run.Id:D}"), ct);
        await notifier.PushAsync(updated, message, ct);
        return updated;
    }

    private static async Task<GccV2AgentTestRunDto> CompleteAsync(
        HttpGccV2Repository repo, GccV2AgentTestProgressNotifier notifier,
        GccV2AgentTestRunDto run, string status, object result, string? error, CancellationToken ct)
    {
        var updated = await repo.PatchAgentTestRunAsync(run.Id, new(
            status, 100, status, JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            error, null, null, "system:agent-test-worker", null, $"agent-test:{run.Id:D}"), ct);
        await notifier.PushAsync(updated, error, ct);
        return updated;
    }
}
