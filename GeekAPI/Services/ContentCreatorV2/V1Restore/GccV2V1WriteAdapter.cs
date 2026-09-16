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

        var generator = GccV2V1ContentTypeRouter.For(job.ContentType);
        if (generator == GccV2V1Generator.Unsupported)
            throw new InvalidOperationException(GccV2V1ContentTypeRouter.UnsupportedMessage(job.ContentType));

        var generated = generator switch
        {
            GccV2V1Generator.Blog => await orchestrator.GenerateBlogAsync(project.Id, null, ct),
            GccV2V1Generator.ToolPage => await orchestrator.GenerateToolPagesAsync(project.Id, null, null, null, ct),
            _ => await orchestrator.GeneratePillarBodyAsync(project.Id, null, ct),
        };

        var toolPost = generator == GccV2V1Generator.ToolPage
            ? generated.ToolPosts?.FirstOrDefault()
            : null;

        var (title, metaDescription, body, keywords, wordCount) = generator switch
        {
            GccV2V1Generator.Blog when generated.Blog is { } b =>
                (b.Title, b.MetaDescription, b.Body, b.Keywords, b.WordCount),
            GccV2V1Generator.ToolPage when toolPost is { } t =>
                (t.Title, t.MetaDescription, t.Body, new List<string>(), t.WordCount),
            _ when generated.Article is { } a =>
                (a.Title, a.MetaDescription, a.Body, a.Keywords, a.WordCount),
            _ => throw new InvalidOperationException(
                $"v1 WRITE ({generator}) produced nothing for project {project.Id}. Nothing to persist."),
        };

        if (body is null)
            throw new InvalidOperationException(
                $"v1 WRITE ({generator}) returned a draft with no body for project {project.Id}.");

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
            "WRITE via v1 ({Generator}) for job {JobId}: '{Title}', {SectionCount} sections, {WordCount} words.",
            generator, job.Id, title, sections.Count, wordCount);

        return new GccV2WriteOutput
        {
            Title = title,
            MetaDescription = metaDescription,
            Lede = lede,
            Sections = sections,
            Keywords = keywords,
        };
    }
}
