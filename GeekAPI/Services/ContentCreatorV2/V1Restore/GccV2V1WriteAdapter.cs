using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreatorV2.V1Restore;

/// <summary>
/// WRITE, written by v1.
///
/// The job worker calls this instead of GccV2WriteService.WriteAsync, so the body of a draft comes
/// from ContentGenerationOrchestrator.GeneratePillarBodyAsync.
///
/// The two systems already share a document model: GccV2WriteOutput.ToContentDocument() returns
/// v1's ContentDocument(Lede, Sections), and v1's ArticleDraft.Body is that same type. So this is a
/// wrap rather than a translation - v1's sections are carried across as-is, with no re-shaping that
/// could drop or invent content.
///
/// VALIDATE, REPAIR and final synthesis continue to run on the V2 side against this output, which is
/// why the shape has to be exact rather than approximate.
/// </summary>
public sealed class GccV2V1WriteAdapter(
    GccV2V1ProjectBridge bridge,
    ContentGenerationOrchestrator orchestrator,
    ILogger<GccV2V1WriteAdapter> logger)
{
    public async Task<GccV2WriteOutput> WriteAsync(GccV2JobDto job, CancellationToken ct)
    {
        var project = await bridge.EnsureProjectForCreateAsync(job.CreateId, ct);

        logger.LogInformation(
            "WRITE via v1 for job {JobId}: project {ProjectId}.", job.Id, project.Id);

        var generated = await orchestrator.GeneratePillarBodyAsync(project.Id, null, ct);

        var article = generated.Article
            ?? throw new InvalidOperationException(
                $"v1 WRITE produced no article for project {project.Id}. Nothing to persist.");

        var body = article.Body
            ?? throw new InvalidOperationException(
                $"v1 WRITE returned an article with no body for project {project.Id}.");

        if (body.Sections.Count == 0)
            throw new InvalidOperationException(
                $"v1 WRITE returned a body with no sections for project {project.Id}.");

        var lede = new GccV2WriteSection(
            SectionKey: "lede",
            Heading: body.Lede.Heading,
            Job: null,
            Section: body.Lede,
            UsedFallbackStub: false);

        var sections = body.Sections
            .Select((section, index) => new GccV2WriteSection(
                SectionKey: $"s{index + 1}",
                Heading: section.Heading,
                Job: null,
                Section: section,
                UsedFallbackStub: false))
            .ToList();

        logger.LogInformation(
            "WRITE via v1 for job {JobId}: '{Title}', {SectionCount} sections, {WordCount} words.",
            job.Id, article.Title, sections.Count, article.WordCount);

        return new GccV2WriteOutput
        {
            Title = article.Title,
            MetaDescription = article.MetaDescription,
            Lede = lede,
            Sections = sections,
            Keywords = article.Keywords,
        };
    }
}
