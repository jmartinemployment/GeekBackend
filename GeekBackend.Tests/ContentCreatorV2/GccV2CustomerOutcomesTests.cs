using GeekRepository.Controllers.ContentCreatorV2;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Evidence-aware customer outcome records — durable CRUD + Jasper evidence gates.
/// </summary>
public sealed class GccV2CustomerOutcomesTests
{
    private const string Owner = "11111111-1111-1111-1111-111111111111";
    private const string Other = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task Create_list_get_delete_are_owner_scoped_and_durable()
    {
        await using var db = Db();
        var controller = new GccV2CustomerOutcomesController(db);

        var created = Assert.IsType<GccV2CustomerOutcome>(Assert.IsType<OkObjectResult>(
            (await controller.Create(ValidCommand(Owner), default)).Result).Value);

        Assert.Equal(Owner, created.OwnerUserId);
        Assert.Equal("Q3 review hours", created.Title);
        Assert.Equal("telemetry-measured", created.EvidenceStatus);
        Assert.Equal(0.7, created.AttributionConfidence);
        Assert.Equal("""["faq-generator@2"]""", created.WorkflowVersionsJson);
        Assert.Equal(40, created.GeneratedCount);
        Assert.Equal("1.8 hours", created.ObservedValue);

        var listed = Assert.IsAssignableFrom<IReadOnlyList<GccV2CustomerOutcome>>(
            Assert.IsType<OkObjectResult>((await controller.List(Owner, default)).Result).Value);
        Assert.Single(listed);
        Assert.Equal(created.Id, listed[0].Id);

        var fetched = Assert.IsType<GccV2CustomerOutcome>(Assert.IsType<OkObjectResult>(
            (await controller.Get(created.Id, Owner, default)).Result).Value);
        Assert.Equal(created.MetricDefinition, fetched.MetricDefinition);

        Assert.IsType<NoContentResult>(
            await controller.Delete(created.Id, Owner, default));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<GccV2CustomerOutcome>>(
            Assert.IsType<OkObjectResult>((await controller.List(Owner, default)).Result).Value));
    }

    [Fact]
    public async Task Owner_isolation_hides_foreign_records()
    {
        await using var db = Db();
        var controller = new GccV2CustomerOutcomesController(db);

        var created = Assert.IsType<GccV2CustomerOutcome>(Assert.IsType<OkObjectResult>(
            (await controller.Create(ValidCommand(Owner), default)).Result).Value);

        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<GccV2CustomerOutcome>>(
            Assert.IsType<OkObjectResult>((await controller.List(Other, default)).Result).Value));
        Assert.IsType<NotFoundResult>((await controller.Get(created.Id, Other, default)).Result);
        Assert.IsType<NotFoundResult>(await controller.Delete(created.Id, Other, default));

        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<GccV2CustomerOutcome>>(
            Assert.IsType<OkObjectResult>((await controller.List(Owner, default)).Result).Value));
    }

    [Theory]
    [InlineData("customer-reported")]
    [InlineData("telemetry-measured")]
    [InlineData("modeled")]
    [InlineData("experimental")]
    [InlineData("independently-audited")]
    [InlineData("TELEMETRY-MEASURED")]
    public async Task Allowed_evidence_statuses_normalize_to_lowercase(string status)
    {
        await using var db = Db();
        var controller = new GccV2CustomerOutcomesController(db);
        var created = Assert.IsType<GccV2CustomerOutcome>(Assert.IsType<OkObjectResult>(
            (await controller.Create(ValidCommand(Owner) with { EvidenceStatus = status }, default))
                .Result).Value);
        Assert.Equal(status.ToLowerInvariant(), created.EvidenceStatus);
    }

    [Fact]
    public async Task Rejects_invalid_evidence_status_period_and_confidence()
    {
        await using var db = Db();
        var controller = new GccV2CustomerOutcomesController(db);

        Assert.IsType<BadRequestObjectResult>((await controller.Create(
            ValidCommand(Owner) with { EvidenceStatus = "vendor-claim" }, default)).Result);

        Assert.IsType<BadRequestObjectResult>((await controller.Create(
            ValidCommand(Owner) with
            {
                PeriodStart = new DateOnly(2026, 9, 30),
                PeriodEnd = new DateOnly(2026, 7, 1),
            }, default)).Result);

        Assert.IsType<BadRequestObjectResult>((await controller.Create(
            ValidCommand(Owner) with { AttributionConfidence = 1.5 }, default)).Result);

        Assert.IsType<BadRequestObjectResult>((await controller.Create(
            ValidCommand(Owner) with { Title = "  " }, default)).Result);

        Assert.IsType<BadRequestObjectResult>((await controller.Create(
            ValidCommand(Owner) with { MetricDefinition = "" }, default)).Result);

        Assert.IsType<BadRequestObjectResult>((await controller.Create(
            ValidCommand(Owner) with { Source = null! }, default)).Result);
    }

    [Fact]
    public async Task Defaults_evidence_status_and_workflow_versions_when_omitted()
    {
        await using var db = Db();
        var controller = new GccV2CustomerOutcomesController(db);
        var created = Assert.IsType<GccV2CustomerOutcome>(Assert.IsType<OkObjectResult>(
            (await controller.Create(ValidCommand(Owner) with
            {
                EvidenceStatus = null,
                WorkflowVersionsJson = null,
                AttributionConfidence = null,
            }, default)).Result).Value);

        Assert.Equal("customer-reported", created.EvidenceStatus);
        Assert.Equal("[]", created.WorkflowVersionsJson);
        Assert.Null(created.AttributionConfidence);
    }

    [Fact]
    public async Task List_orders_by_period_end_descending()
    {
        await using var db = Db();
        var controller = new GccV2CustomerOutcomesController(db);

        await controller.Create(ValidCommand(Owner) with
        {
            Title = "Older",
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
        }, default);
        await controller.Create(ValidCommand(Owner) with
        {
            Title = "Newer",
            PeriodStart = new DateOnly(2026, 7, 1),
            PeriodEnd = new DateOnly(2026, 9, 30),
        }, default);

        var listed = Assert.IsAssignableFrom<IReadOnlyList<GccV2CustomerOutcome>>(
            Assert.IsType<OkObjectResult>((await controller.List(Owner, default)).Result).Value);
        Assert.Equal(2, listed.Count);
        Assert.Equal("Newer", listed[0].Title);
        Assert.Equal("Older", listed[1].Title);
    }

    private static GccV2CustomerOutcomesController.CreateCommand ValidCommand(string owner) =>
        new(
            OwnerUserId: owner,
            Title: "Q3 review hours",
            MetricDefinition: "Median hours from RFP intake to first draft.",
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: new DateOnly(2026, 9, 30),
            Baseline: "12 hours",
            Denominator: "per quarterly RFP",
            ObservedValue: "1.8 hours",
            Source: "Ops time-tracking + TaskRun telemetry",
            EvidenceStatus: "telemetry-measured",
            AttributionMethod: "before/after same reviewer cohort",
            AttributionConfidence: 0.7,
            WorkflowVersionsJson: """["faq-generator@2"]""",
            GeneratedCount: 40,
            AcceptedCount: 28,
            PublishedCount: 18,
            RejectedCount: 5,
            ReviewMinutes: 220,
            Notes: "Not a cash ROI claim.");

    private static ContentCreatorV2DbContext Db() => new(
        new DbContextOptionsBuilder<ContentCreatorV2DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
