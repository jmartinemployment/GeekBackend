using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The operator's own site structure around the keyword -- the headings their site already carries
/// under it, so the piece covers what the site says it covers and can point at pages that exist.
/// </summary>
/// <remarks>
/// One definition for both Generate routes, the create-keyed one and the project one.
///
/// This read the retired Site Analyzer until 2026-09-23, and passed it the wrong kind of id:
/// ProjectSiteRunId is a Geek-Crawler-v2 run id and GetPageSectionTreesAsync wanted a Site Analyzer
/// profile id. Site Analyzer's routes were deleted (582a171, 5072820), so the call could only fail, and
/// it failed to null. Generation then proceeded with no site structure at all, on every create,
/// silently. A bearer check in front of it was a second silent null, and a worse one once generate ran
/// as a background job where no bearer is captured.
///
/// This is the same read the live project-site/runs/{runId}/hierarchy-match route does. Blocks, never
/// Html. Null -- no injection, no failure -- when there is no site run, no keyword, no pages, or no
/// match: an uncertain match must never block Generate or inject a guessed subtree.
/// </remarks>
public sealed class GccMustMentionBlockBuilder
{
    private readonly GccProjectSiteStructureReader _siteStructure;
    private readonly ILogger<GccMustMentionBlockBuilder> _logger;

    public GccMustMentionBlockBuilder(
        GccProjectSiteStructureReader siteStructure,
        ILogger<GccMustMentionBlockBuilder> logger)
    {
        _siteStructure = siteStructure;
        _logger = logger;
    }

    public async Task<string?> BuildAsync(GccCreateDto create, CancellationToken ct)
    {
        if (create.ProjectSiteRunId is not Guid runId || runId == Guid.Empty)
            return null;
        if (string.IsNullOrWhiteSpace(create.Topic))
            return null;

        var structure = await _siteStructure.ReadAsync(runId, ct).ConfigureAwait(false);
        if (structure is null)
        {
            _logger.LogInformation(
                "Site structure: run {RunId} returned no pages, so this run generates without it.", runId);
            return null;
        }

        var matches = GccSiteStructureMatch.MatchAll(structure, [create.Topic.Trim()]);
        var matched = matches.FirstOrDefault(m => m.ChildHeadings.Length > 0) ?? matches.FirstOrDefault();
        if (matched is null)
        {
            _logger.LogInformation(
                "Site structure: nothing on the site matches \"{Topic}\", so this run generates without it.",
                create.Topic);
            return null;
        }

        // Formatted by GccMustMention so the guard can read back the subtopics it names -- they are
        // compulsory, and a heading covering one needs a source to be licensed against.
        return GccMustMention.Format(matched.MatchedHeading, matched.SourcePageUrl, matched.ChildHeadings);
    }
}
