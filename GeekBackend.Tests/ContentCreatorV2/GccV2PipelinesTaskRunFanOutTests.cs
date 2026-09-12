using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Pipelines;
using GeekRepository.Controllers.ContentCreatorV2;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2PipelinesTaskRunFanOutTests
{
    [Fact]
    public async Task StartRun_creates_task_runs_for_task_agent_stages_not_handoffs_or_roi()
    {
        await using var db = Db();
        await SeedPublishedAgentAsync(db, "query-planner", "query-plan", "queryPlan.v1");
        await SeedPublishedAgentAsync(db, "faq-generator", "faq-set", "faqSet.v1");
        await SeedPublishedAgentAsync(db, "ai-readiness", "readiness-score", "readinessScore.v1");

        var controller = new GccV2PipelinesController(db);
        const string owner = "11111111-1111-1111-1111-111111111111";
        var stagesJson = GccV2PipelineStagePolicy.DefaultAeoTemplateStagesJson();
        var digest = GccV2PipelineStagePolicy.Digest(stagesJson, "{}");

        var created = Assert.IsType<GccV2PipelinesController.PipelineGraph>(
            Assert.IsType<OkObjectResult>((await controller.Create(
                new(owner, "AEO fan-out", null, stagesJson, digest, Publish: true), default)).Result).Value);

        var afterRun = Assert.IsType<GccV2PipelinesController.PipelineGraph>(
            Assert.IsType<OkObjectResult>((await controller.StartRun(
                created.Id,
                new(owner, owner, """{"topic":"Evidence Engine"}""", WorkItemInputsJson:
                [
                    """{"topic":"Evidence Engine"}""",
                    """{"topic":"RAG grounding"}""",
                ]),
                default)).Result).Value);

        Assert.Single(afterRun.Runs);
        Assert.Equal("succeeded", afterRun.Runs[0].Status);
        Assert.Equal(2, afterRun.Runs[0].WorkItems.Count);

        // 2 work items × 3 task-agent stages (query/faq/readiness); ROI + handoffs do not create TaskRuns.
        Assert.Equal(6, await db.GccV2TaskRuns.CountAsync(r => r.OwnerUserId == owner));

        foreach (var item in afterRun.Runs[0].WorkItems)
        {
            var taskAgentAttempts = item.StageAttempts
                .Where(a => a.Kind == "task-agent" && a.CapabilityId != "roi-business-calculator")
                .ToList();
            Assert.Equal(3, taskAgentAttempts.Count);
            Assert.All(taskAgentAttempts, a =>
            {
                Assert.Equal("succeeded", a.Status);
                Assert.NotNull(a.TaskRunId);
                Assert.NotNull(a.ArtifactVersionId);
                using var output = JsonDocument.Parse(a.OutputJson!);
                Assert.Equal("task-run", output.RootElement.GetProperty("mode").GetString());
                Assert.False(string.IsNullOrWhiteSpace(output.RootElement.GetProperty("taskRunId").GetString()));
                Assert.DoesNotContain(".stub.v1", output.RootElement.GetProperty("artifactType").GetString());
            });

            var roi = Assert.Single(item.StageAttempts, a => a.CapabilityId == "roi-business-calculator");
            Assert.Null(roi.TaskRunId);
            using var roiOutput = JsonDocument.Parse(roi.OutputJson!);
            Assert.Equal("roiProjection.v1", roiOutput.RootElement.GetProperty("artifactType").GetString());

            Assert.All(item.StageAttempts.Where(a => a.Kind == "handoff"), a => Assert.Null(a.TaskRunId));
            var canvas = Assert.Single(item.StageAttempts, a => a.Handoff == "canvas");
            Assert.Equal("succeeded", canvas.Status);
            using var canvasOutput = JsonDocument.Parse(canvas.OutputJson!);
            Assert.Equal("canvas-attach", canvasOutput.RootElement.GetProperty("mode").GetString());
            Assert.False(string.IsNullOrWhiteSpace(canvasOutput.RootElement.GetProperty("projectId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(canvasOutput.RootElement.GetProperty("assetId").GetString()));

            var publish = Assert.Single(item.StageAttempts, a => a.Handoff == "publish");
            using var publishOutput = JsonDocument.Parse(publish.OutputJson!);
            Assert.Equal("publish-ready", publishOutput.RootElement.GetProperty("mode").GetString());
            Assert.Equal(
                canvasOutput.RootElement.GetProperty("projectId").GetString(),
                publishOutput.RootElement.GetProperty("projectId").GetString());
        }

        Assert.Equal(1, await db.GccV2CanvasProjects.CountAsync(p => p.OwnerUserId == owner));
        Assert.Equal(2, await db.GccV2CanvasAssets.CountAsync());
    }

    [Fact]
    public async Task StartRun_isolation_failure_skips_later_stages_without_orphan_task_runs()
    {
        await using var db = Db();
        await SeedPublishedAgentAsync(db, "query-planner", "query-plan", "queryPlan.v1");
        await SeedPublishedAgentAsync(db, "faq-generator", "faq-set", "faqSet.v1");
        await SeedPublishedAgentAsync(db, "ai-readiness", "readiness-score", "readinessScore.v1");

        var controller = new GccV2PipelinesController(db);
        const string owner = "22222222-2222-2222-2222-222222222222";
        var stagesJson = GccV2PipelineStagePolicy.DefaultAeoTemplateStagesJson();
        var digest = GccV2PipelineStagePolicy.Digest(stagesJson, "{}");

        var created = Assert.IsType<GccV2PipelinesController.PipelineGraph>(
            Assert.IsType<OkObjectResult>((await controller.Create(
                new(owner, "AEO fail", null, stagesJson, digest, Publish: true), default)).Result).Value);

        var afterRun = Assert.IsType<GccV2PipelinesController.PipelineGraph>(
            Assert.IsType<OkObjectResult>((await controller.StartRun(
                created.Id,
                new(owner, owner, """{"topic":"Evidence Engine"}""", FailStageKey: "create-faq"),
                default)).Result).Value);

        Assert.Equal("failed", afterRun.Runs[0].Status);
        var attempts = afterRun.Runs[0].WorkItems[0].StageAttempts;
        Assert.Equal("succeeded", Assert.Single(attempts, a => a.StageKey == "plan-queries").Status);
        Assert.Equal("failed", Assert.Single(attempts, a => a.StageKey == "create-faq").Status);
        Assert.All(attempts.Where(a => a.StageKey is not ("plan-queries" or "create-faq")),
            a => Assert.Equal("skipped", a.Status));

        // Only plan-queries TaskRun; failed/skipped stages must not create TaskRuns.
        Assert.Equal(1, await db.GccV2TaskRuns.CountAsync(r => r.OwnerUserId == owner));
        Assert.NotNull(Assert.Single(attempts, a => a.StageKey == "plan-queries").TaskRunId);
        Assert.Null(Assert.Single(attempts, a => a.StageKey == "create-faq").TaskRunId);
    }

    private static async Task SeedPublishedAgentAsync(
        ContentCreatorV2DbContext db, string capabilityId, string endpoint, string artifactType)
    {
        var agents = new GccV2TaskAgentsController(db);
        var definition = Assert.IsType<GccV2TaskAgentDefinition>(Assert.IsType<CreatedAtActionResult>(
            (await agents.Create(new(
                capabilityId, capabilityId, $"{capabilityId} for pipeline tests.", "admin"),
                default)).Result).Value);
        var version = Assert.IsType<GccV2TaskAgentVersion>(Assert.IsType<CreatedAtActionResult>(
            (await agents.CreateVersion(definition.Id, new(
                "1.0.0", "originate",
                """{"contentType":["page"],"funnelStage":["awareness"],"marketingFunction":["seo"],"process":["originate"]}""",
                """{"additionalProperties":false,"properties":{"topic":{"type":"string"}},"required":["topic"],"type":"object"}""",
                """{"properties":{"summary":{"type":"string"}},"type":"object"}""",
                $$"""{"engine":"geek-crawler-rag","endpoint":"{{endpoint}}","artifactType":"{{artifactType}}"}""",
                """{"manifest":"optional","version":"context-policy.v1"}""",
                """{"component":"generic","shell":"gcc-task-result-shell.v1"}""",
                """{"downstream":[],"upstream":[]}""",
                """[]""", """["o3"]""", "[]",
                """{"minimumScore":0.8}""", "admin"), default)).Result).Value);
        Assert.IsType<OkObjectResult>((await agents.TransitionVersion(
            version.Id, "publish", new("admin", null), default)).Result);
    }

    private static ContentCreatorV2DbContext Db() => new(
        new DbContextOptionsBuilder<ContentCreatorV2DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
