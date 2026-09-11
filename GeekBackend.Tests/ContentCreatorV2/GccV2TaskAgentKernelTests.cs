using GeekRepository.Controllers.ContentCreatorV2;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2TaskAgentKernelTests
{
    [Fact]
    public async Task Version_contract_is_canonical_digest_pinned_and_published_is_immutable()
    {
        await using var db = Db();
        var controller = new GccV2TaskAgentsController(db);
        var definition = Assert.IsType<GccV2TaskAgentDefinition>(Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(new("fact-density", "Fact Density", "Measure factual density", "admin"),
                default)).Result).Value);
        var version = Assert.IsType<GccV2TaskAgentVersion>(Assert.IsType<CreatedAtActionResult>(
            (await controller.CreateVersion(definition.Id, VersionCommand(), default)).Result).Value);

        Assert.Equal(64, version.VersionDigest.Length);
        Assert.Equal(64, version.InputSchemaDigest.Length);
        Assert.NotEqual(version.InputSchemaDigest, version.OutputSchemaDigest);
        Assert.IsType<OkObjectResult>((await controller.TransitionVersion(
            version.Id, "publish", new("admin", null), default)).Result);
        Assert.IsType<ConflictObjectResult>((await controller.Patch(
            definition.Id, new("Changed", null, "admin"), default)).Result);
    }

    [Fact]
    public async Task Durable_run_claim_cancel_and_artifact_lineage_are_owner_scoped()
    {
        await using var db = Db();
        var agents = new GccV2TaskAgentsController(db);
        var definition = Assert.IsType<GccV2TaskAgentDefinition>(Assert.IsType<CreatedAtActionResult>(
            (await agents.Create(new("gap-report", "Gap Report", "Find gaps", "admin"), default)).Result).Value);
        var version = Assert.IsType<GccV2TaskAgentVersion>(Assert.IsType<CreatedAtActionResult>(
            (await agents.CreateVersion(definition.Id, VersionCommand(), default)).Result).Value);
        await agents.TransitionVersion(version.Id, "publish", new("admin", null), default);

        const string owner = "11111111-1111-1111-1111-111111111111";
        const string input = """{"topic":"ai"}""";
        const string empty = "{}";
        var runs = new GccV2TaskRunsController(db);
        var run = Assert.IsType<GccV2TaskRun>(Assert.IsType<CreatedAtActionResult>(
            (await runs.Create(new(
                owner, definition.Id, version.Id, version.VersionDigest,
                input, Sha(input), null, null,
                empty, Sha(empty), empty, Sha(empty), empty, Sha(empty), null, owner), default)).Result).Value);
        Assert.Equal(run.Id, run.RootRunId);
        Assert.IsType<NotFoundResult>((await runs.Get(run.Id, Guid.NewGuid().ToString(), default)).Result);
        var claimed = Assert.IsType<GccV2TaskRun>(Assert.IsType<OkObjectResult>(
            (await runs.Claim(run.Id, "worker-a", 120, default)).Result).Value);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.IsType<ConflictObjectResult>((await runs.Claim(run.Id, "worker-b", 120, default)).Result);

        var artifact = Assert.IsType<GccV2TaskArtifact>(Assert.IsType<CreatedAtActionResult>(
            (await runs.AddArtifact(run.Id, new(owner, "gapReport.v1", owner), default)).Result).Value);
        var first = Assert.IsType<GccV2TaskArtifactVersion>(Assert.IsType<CreatedAtActionResult>(
            (await runs.AddArtifactVersion(artifact.Id, new(
                owner, """{"gaps":[]}""", "[]", "[]", "valid", "{}", [], owner), default)).Result).Value);
        var second = Assert.IsType<GccV2TaskArtifactVersion>(Assert.IsType<CreatedAtActionResult>(
            (await runs.AddArtifactVersion(artifact.Id, new(
                owner, """{"gaps":[{"name":"proof"}]}""", "[]", "[]", "valid", "{}",
                [first.Id], owner), default)).Result).Value);
        Assert.Equal(2, second.VersionNumber);
        Assert.Single(second.Parents);
        Assert.Equal(first.Id, second.Parents[0].ParentArtifactVersionId);

        var cancelled = Assert.IsType<GccV2TaskRun>(Assert.IsType<OkObjectResult>(
            (await runs.Transition(run.Id, new(
                "cancelled", "cancelled", 100, "cancelled", "{}", owner, "worker-a",
                null, null, true), default)).Result).Value);
        Assert.Equal("cancelled", cancelled.Status);
        Assert.NotNull(cancelled.CancelledAtUtc);
        Assert.IsType<ConflictObjectResult>((await runs.AddArtifact(
            run.Id, new(owner, "gapReport.v1", "worker-a", "worker-a"), default)).Result);
        Assert.IsType<ConflictObjectResult>((await runs.AddArtifactVersion(
            artifact.Id, new(
                owner, """{"gaps":[]}""", "[]", "[]", "valid", "{}", [], "worker-a",
                ExpectedClaimedBy: "worker-a"), default)).Result);
    }

    [Fact]
    public async Task Expired_lease_allows_cross_instance_reclaim_and_bumps_recovery()
    {
        await using var db = Db();
        var agents = new GccV2TaskAgentsController(db);
        var definition = Assert.IsType<GccV2TaskAgentDefinition>(Assert.IsType<CreatedAtActionResult>(
            (await agents.Create(new("ai-readiness", "AI Readiness", "Score readiness", "admin"),
                default)).Result).Value);
        var version = Assert.IsType<GccV2TaskAgentVersion>(Assert.IsType<CreatedAtActionResult>(
            (await agents.CreateVersion(definition.Id, VersionCommand(), default)).Result).Value);
        await agents.TransitionVersion(version.Id, "publish", new("admin", null), default);

        const string owner = "11111111-1111-1111-1111-111111111111";
        const string input = """{"topic":"ai"}""";
        const string empty = "{}";
        var runs = new GccV2TaskRunsController(db);
        var run = Assert.IsType<GccV2TaskRun>(Assert.IsType<CreatedAtActionResult>(
            (await runs.Create(new(
                owner, definition.Id, version.Id, version.VersionDigest,
                input, Sha(input), null, null,
                empty, Sha(empty), empty, Sha(empty), empty, Sha(empty), null, owner), default)).Result).Value);

        var claimed = Assert.IsType<GccV2TaskRun>(Assert.IsType<OkObjectResult>(
            (await runs.Claim(run.Id, "worker-a", 120, default)).Result).Value);
        Assert.Equal("running", claimed.Status);
        Assert.Equal("worker-a", claimed.ClaimedByInstanceId);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.Equal(0, claimed.RecoveryCount);
        Assert.IsType<ConflictObjectResult>((await runs.Claim(run.Id, "worker-b", 120, default)).Result);

        // Expire the active lease so another instance can reclaim.
        var expired = Assert.IsType<GccV2TaskRun>(Assert.IsType<OkObjectResult>(
            (await runs.Transition(run.Id, new(
                "running", "analyzing", 40, "progress", "{}", "worker-a",
                ExpectedClaimedBy: "worker-a",
                LeaseUntilUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                TerminalError: null), default)).Result).Value);
        Assert.True(expired.LeaseUntilUtc < DateTimeOffset.UtcNow);

        var recovered = Assert.IsType<GccV2TaskRun>(Assert.IsType<OkObjectResult>(
            (await runs.Claim(run.Id, "recovery-worker", 120, default)).Result).Value);
        Assert.Equal("recovery-worker", recovered.ClaimedByInstanceId);
        Assert.Equal(2, recovered.AttemptCount);
        Assert.Equal(1, recovered.RecoveryCount);
        Assert.True(recovered.LeaseUntilUtc > DateTimeOffset.UtcNow);

        Assert.IsType<ConflictObjectResult>(
            (await runs.Claim(run.Id, "worker-c", 120, default)).Result);

        var events = Assert.IsAssignableFrom<IReadOnlyList<GccV2TaskRunEvent>>(
            Assert.IsType<OkObjectResult>((await runs.Events(run.Id, 0, default)).Result).Value);
        Assert.Contains(events, e => e.Type == "claimed" && e.Actor == "worker-a");
        Assert.Contains(events, e => e.Type == "recovered" && e.Actor == "recovery-worker");
    }

    private static GccV2TaskAgentsController.CreateVersionCommand VersionCommand() => new(
        "1.0.0", "analysis",
        """{"contentType":["blog"],"funnelStage":["consideration"],"marketingFunction":["seo"],"process":["audit"]}""",
        """{"additionalProperties":false,"properties":{"topic":{"type":"string"}},"required":["topic"],"type":"object"}""",
        """{"properties":{"score":{"type":"number"}},"required":["score"],"type":"object"}""",
        """{"steps":[{"id":"analyze"}],"version":"workflow.v1"}""",
        """{"manifest":"optional","version":"context-policy.v1"}""",
        """{"component":"gap-report","shell":"gcc-task-result-shell.v1"}""",
        """{"downstream":["pillarArticle.v1"],"upstream":["entityMap.v1"]}""",
        """["search_corpus"]""", """["o3"]""", "[]",
        """{"citationCoverage":0.9,"minimumScore":0.8}""", "admin");

    private static string Sha(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static ContentCreatorV2DbContext Db() => new(
        new DbContextOptionsBuilder<ContentCreatorV2DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
