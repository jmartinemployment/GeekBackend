using System.Text.Json;
using GeekRepository.Controllers.ContentCreatorV2;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2GridsTests
{
    [Fact]
    public async Task Create_seedDemo_and_sample_run_creates_task_runs_per_row()
    {
        await using var db = Db();
        await SeedPublishedFaqAgentAsync(db);
        var controller = new GccV2GridsController(db);
        const string owner = "11111111-1111-1111-1111-111111111111";

        var created = Assert.IsType<GccV2Grid>(Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(
                new(owner, "FAQ launch batch", SeedDemo: true), default)).Result).Value);
        Assert.Equal(owner, created.OwnerUserId);
        Assert.Equal("FAQ launch batch", created.Name);
        Assert.Equal(12, created.Rows.Count);
        Assert.Equal("ready", created.Status);
        Assert.Contains("faq-generator", created.ConfigJson);

        var listed = Assert.IsAssignableFrom<IReadOnlyList<GccV2GridsController.GridListItem>>(
            Assert.IsType<OkObjectResult>((await controller.List(owner, default)).Result).Value);
        Assert.Single(listed);
        Assert.Equal(12, listed[0].RowCount);
        Assert.Null(listed[0].LastRunStatus);

        var afterRun = Assert.IsType<GccV2Grid>(Assert.IsType<OkObjectResult>(
            (await controller.CreateRun(created.Id, new(owner, "sample"), default)).Result).Value);
        Assert.Single(afterRun.Runs);
        Assert.Equal("succeeded", afterRun.Runs[0].Status);
        Assert.Equal(10, afterRun.Runs[0].OutputCount);
        Assert.Equal("sample", afterRun.Runs[0].Mode);
        Assert.Equal(10, afterRun.Runs[0].SampleSize);

        var succeeded = afterRun.Rows.Where(r => r.Status == "succeeded").OrderBy(r => r.RowIndex).ToList();
        var pending = afterRun.Rows.Where(r => r.Status == "pending").OrderBy(r => r.RowIndex).ToList();
        Assert.Equal(10, succeeded.Count);
        Assert.Equal(2, pending.Count);
        Assert.Equal(0, succeeded[0].RowIndex);
        Assert.Equal(9, succeeded[^1].RowIndex);
        Assert.Equal(10, pending[0].RowIndex);

        using var output = JsonDocument.Parse(succeeded[0].OutputJson!);
        Assert.Equal("task-run", output.RootElement.GetProperty("mode").GetString());
        Assert.Equal("faq-generator", output.RootElement.GetProperty("capability").GetString());
        Assert.Equal("faq-set", output.RootElement.GetProperty("endpoint").GetString());
        Assert.False(string.IsNullOrWhiteSpace(output.RootElement.GetProperty("taskRunId").GetString()));
        Assert.StartsWith("FAQ draft for:", output.RootElement.GetProperty("result").GetString());
        Assert.Equal("deterministic", output.RootElement.GetProperty("artifactSource").GetString());

        Assert.Equal(10, await db.GccV2TaskRuns.CountAsync(r => r.OwnerUserId == owner));
        Assert.All(
            await db.GccV2TaskRuns.Where(r => r.OwnerUserId == owner).ToListAsync(),
            r => Assert.Equal("succeeded", r.Status));

        using var budget = JsonDocument.Parse(afterRun.Runs[0].BudgetPreviewJson);
        Assert.Equal(1, budget.RootElement.GetProperty("creditsPerRow").GetInt32());
        Assert.Equal(10, budget.RootElement.GetProperty("rowCount").GetInt32());
        Assert.Equal(10, budget.RootElement.GetProperty("estimatedCredits").GetInt32());
        Assert.Equal("task-run", budget.RootElement.GetProperty("execution").GetString());

        Assert.Equal("ready", afterRun.Status);

        var full = Assert.IsType<GccV2Grid>(Assert.IsType<OkObjectResult>(
            (await controller.CreateRun(created.Id, new(owner, "full"), default)).Result).Value);
        Assert.Equal(2, full.Runs.Count);
        Assert.Equal(12, full.Runs[0].OutputCount);
        Assert.All(full.Rows, r => Assert.Equal("succeeded", r.Status));
        Assert.Equal("complete", full.Status);
        Assert.Equal(22, await db.GccV2TaskRuns.CountAsync(r => r.OwnerUserId == owner));
    }

    [Fact]
    public async Task Sample_run_fails_rows_when_no_published_agent_exists()
    {
        await using var db = Db();
        var controller = new GccV2GridsController(db);
        const string owner = "11111111-1111-1111-1111-111111111111";

        var created = Assert.IsType<GccV2Grid>(Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(
                new(owner, "FAQ launch batch", SeedDemo: true), default)).Result).Value);

        var afterRun = Assert.IsType<GccV2Grid>(Assert.IsType<OkObjectResult>(
            (await controller.CreateRun(created.Id, new(owner, "sample"), default)).Result).Value);

        Assert.Equal("failed", afterRun.Runs[0].Status);
        Assert.Equal(0, afterRun.Runs[0].OutputCount);
        Assert.Equal(10, afterRun.Rows.Count(r => r.Status == "failed"));
        Assert.Equal(2, afterRun.Rows.Count(r => r.Status == "pending"));
        Assert.Contains("No published task agent", afterRun.Rows.First(r => r.Status == "failed").Error);
        Assert.Empty(await db.GccV2TaskRuns.Where(r => r.OwnerUserId == owner).ToListAsync());
    }

    [Fact]
    public async Task Faq_set_capability_alias_resolves_faq_generator_endpoint()
    {
        await using var db = Db();
        await SeedPublishedFaqAgentAsync(db);
        var controller = new GccV2GridsController(db);
        const string owner = "11111111-1111-1111-1111-111111111111";

        var created = Assert.IsType<GccV2Grid>(Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(new(owner, "Alias grid"), default)).Result).Value);
        Assert.IsType<OkObjectResult>((await controller.Patch(created.Id, new(
            owner,
            ConfigJson: """{"columns":[{"key":"topic","kind":"input","label":"Topic"},{"key":"agent","kind":"agent","label":"FAQ","capability":"faq-set"},{"key":"output","kind":"output","label":"Result"}],"creditsPerRow":1}"""),
            default)).Result);
        Assert.IsType<CreatedAtActionResult>((await controller.CreateRow(
            created.Id, new(owner, """{"topic":"What is Evidence Engine?"}"""), default)).Result);

        var afterRun = Assert.IsType<GccV2Grid>(Assert.IsType<OkObjectResult>(
            (await controller.CreateRun(created.Id, new(owner, "full"), default)).Result).Value);
        Assert.Equal("succeeded", afterRun.Runs[0].Status);
        using var output = JsonDocument.Parse(afterRun.Rows[0].OutputJson!);
        Assert.Equal("task-run", output.RootElement.GetProperty("mode").GetString());
        Assert.Equal("faq-generator", output.RootElement.GetProperty("capability").GetString());
        Assert.Equal("faq-set", output.RootElement.GetProperty("endpoint").GetString());
        Assert.Equal("deterministic", output.RootElement.GetProperty("artifactSource").GetString());
    }

    [Fact]
    public async Task CreateRun_uses_supplied_row_artifact_payload_when_present()
    {
        await using var db = Db();
        await SeedPublishedFaqAgentAsync(db);
        var controller = new GccV2GridsController(db);
        const string owner = "11111111-1111-1111-1111-111111111111";

        var created = Assert.IsType<GccV2Grid>(Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(new(owner, "RAG override"), default)).Result).Value);
        var row = Assert.IsType<GccV2GridRow>(Assert.IsType<CreatedAtActionResult>(
            (await controller.CreateRow(
                created.Id, new(owner, """{"topic":"Evidence Engine"}"""), default)).Result).Value);

        var ragPayload = """
            {"artifactType":"faqSet.v1","pairs":[{"question":"What is Evidence Engine?","answer":"A citeable research layer."}],"methodology":"faq-generator-heuristic.v1"}
            """;
        var afterRun = Assert.IsType<GccV2Grid>(Assert.IsType<OkObjectResult>(
            (await controller.CreateRun(created.Id, new(
                owner, "full",
                RowArtifactJsonByRowId: new Dictionary<string, string>
                {
                    [row.Id.ToString("D")] = ragPayload,
                }), default)).Result).Value);

        using var output = JsonDocument.Parse(afterRun.Rows[0].OutputJson!);
        Assert.Equal("rag", output.RootElement.GetProperty("artifactSource").GetString());
        Assert.Equal(
            "What is Evidence Engine?",
            output.RootElement.GetProperty("artifact").GetProperty("pairs")[0].GetProperty("question").GetString());
        Assert.StartsWith("FAQ draft for: What is Evidence Engine?", output.RootElement.GetProperty("result").GetString());
    }

    [Fact]
    public async Task Owner_isolation_returns_404_for_non_owner()
    {
        await using var db = Db();
        var controller = new GccV2GridsController(db);
        const string owner = "11111111-1111-1111-1111-111111111111";
        const string other = "22222222-2222-2222-2222-222222222222";

        var created = Assert.IsType<GccV2Grid>(Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(new(owner, "Private"), default)).Result).Value);

        Assert.IsType<NotFoundResult>((await controller.Get(created.Id, other, default)).Result);
        Assert.IsType<NotFoundResult>((await controller.Patch(
            created.Id, new(other, Name: "Hacked"), default)).Result);
        Assert.IsType<NotFoundResult>((await controller.CreateRow(
            created.Id, new(other, """{"topic":"x"}"""), default)).Result);
        Assert.IsType<NotFoundResult>((await controller.CreateRun(
            created.Id, new(other, "sample"), default)).Result);

        var otherList = Assert.IsAssignableFrom<IReadOnlyList<GccV2GridsController.GridListItem>>(
            Assert.IsType<OkObjectResult>((await controller.List(other, default)).Result).Value);
        Assert.Empty(otherList);
    }

    private static async Task SeedPublishedFaqAgentAsync(ContentCreatorV2DbContext db)
    {
        var agents = new GccV2TaskAgentsController(db);
        var definition = Assert.IsType<GccV2TaskAgentDefinition>(Assert.IsType<CreatedAtActionResult>(
            (await agents.Create(new(
                "faq-generator", "FAQ Generator",
                "Generate answer-first FAQ pairs.", "admin"), default)).Result).Value);
        var version = Assert.IsType<GccV2TaskAgentVersion>(Assert.IsType<CreatedAtActionResult>(
            (await agents.CreateVersion(definition.Id, new(
                "1.0.0", "originate",
                """{"contentType":["faq"],"funnelStage":["consideration"],"marketingFunction":["seo"],"process":["originate"]}""",
                """{"additionalProperties":false,"properties":{"topic":{"type":"string"}},"required":["topic"],"type":"object"}""",
                """{"properties":{"pairs":{"type":"array"}},"required":["pairs"],"type":"object"}""",
                """{"engine":"geek-crawler-rag","endpoint":"faq-set","artifactType":"faqSet.v1"}""",
                """{"manifest":"optional","version":"context-policy.v1"}""",
                """{"component":"faq-list","shell":"gcc-task-result-shell.v1"}""",
                """{"downstream":["schemaMarkup.v1"],"upstream":["queryPlan.v1"]}""",
                """[]""", """["o3"]""", "[]",
                """{"minimumScore":0.8}""", "admin"), default)).Result).Value);
        Assert.IsType<OkObjectResult>((await agents.TransitionVersion(
            version.Id, "publish", new("admin", null), default)).Result);
    }

    private static ContentCreatorV2DbContext Db() => new(
        new DbContextOptionsBuilder<ContentCreatorV2DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
