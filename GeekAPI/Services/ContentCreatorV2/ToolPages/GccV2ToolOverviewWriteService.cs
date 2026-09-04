using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Adapters;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

public sealed class GccV2ToolOverviewWriteService
{
    private readonly HttpGccV2Repository _repo;
    private readonly GccV2ToolPagePromptBuilder _prompts;
    private readonly GccV2ToolPageSpawnService _spawn;
    private readonly GccV2ContextAdapter _contextAdapter;
    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccV2ToolOverviewWriteService> _logger;

    public GccV2ToolOverviewWriteService(
        HttpGccV2Repository repo,
        GccV2ToolPagePromptBuilder prompts,
        GccV2ToolPageSpawnService spawn,
        GccV2ContextAdapter contextAdapter,
        IOptions<CompanyProfileOptions> company,
        ILogger<GccV2ToolOverviewWriteService> logger)
    {
        _repo = repo;
        _prompts = prompts;
        _spawn = spawn;
        _contextAdapter = contextAdapter;
        _company = company.Value;
        _logger = logger;
    }

    public async Task<GccV2WriteOutput> WriteAsync(
        GccV2WriteContext wc,
        Guid ownerUserId,
        GccV2ToolPageTarget target,
        CancellationToken ct)
    {
        var jobs = await _repo.ListJobsByCreateAsync(wc.Job.CreateId, ct);
        var hasPillarJob = jobs.Any(j =>
            string.Equals(j.ContentType, "pillar", StringComparison.OrdinalIgnoreCase));

        PillarSnapshot? pillar = null;
        if (hasPillarJob)
        {
            pillar = await ResolvePillarAsync(wc.Job.CreateId, ct);
            if (pillar is null)
            {
                throw new GccV2ToolWriteDeferredException(
                    "Overview tool page is waiting for the pillar draft to reach ready.");
            }
        }

        var spawnResult = await _spawn.EnsurePartnersSpawnedAsync(wc.Job, ct);
        if (spawnResult.FailureReason is not null)
        {
            throw new InvalidOperationException(
                $"Partner tool spawn failed: {spawnResult.FailureReason}");
        }

        var partnerRows = await LoadPartnerResearchAsync(wc.Job.CreateId, ct);
        if (partnerRows.Count == 0)
        {
            _logger.LogWarning(
                "No partner tool jobs found for overview job {JobId} — writing keyword-only fallback.",
                wc.Job.Id);
        }

        var outlineSections = wc.Outline.Sections;
        if (outlineSections.Count == 0)
            throw new InvalidOperationException("Tool overview job has no approved outline sections.");

        var keyword = wc.BaseContext.TargetKeyword;
        var slug = string.IsNullOrWhiteSpace(target.Slug)
            ? GccV2ToolSlugHelper.SlugifyKeyword(keyword)
            : target.Slug;
        var toolsHeading = ResolveToolsHeading(outlineSections, keyword);
        var headings = outlineSections.Select(s => s.Heading).ToList();
        var metadata = new ArticleMetadataDraft(
            $"Tools for {keyword}",
            pillar?.MetaDescription ?? $"Overview of tools and capabilities for {keyword}.",
            [keyword],
            headings);

        var tokens = 0;
        GccV2WriteSection? ledeWrite = null;
        var sections = new List<GccV2WriteSection>();
        var bodySections = new List<Section>();
        var partnerNames = partnerRows.Select(p => p.Name).ToList();

        for (var i = 0; i < outlineSections.Count; i++)
        {
            var entry = outlineSections[i];
            if (string.Equals(entry.Job, "faq", StringComparison.OrdinalIgnoreCase))
                continue;

            Section section;
            if (IsToolsIndexHeading(entry.Heading, toolsHeading))
            {
                var (toolsSection, toolsTokens) = await BuildToolsIndexSectionAsync(
                    wc, metadata, entry.Heading, toolsHeading, partnerRows, partnerNames, ct);
                section = toolsSection;
                tokens += toolsTokens;
            }
            else
            {
                var sectionContext = _contextAdapter.WithSectionAssignment(
                    wc.BaseContext, entry.Heading, entry.Job, entry.HierarchyChildHeadings);
                var (drafted, sectionTokens) = await DraftOverviewSectionAsync(
                    wc, sectionContext, metadata, entry, i, outlineSections.Count, headings, pillar?.Excerpt, ct);
                section = drafted;
                tokens += sectionTokens;
            }

            section = section with { Heading = entry.Heading, Tag = "h2" };
            var write = new GccV2WriteSection(
                entry.Key,
                entry.Heading,
                entry.Job ?? (i == 0 ? "problem" : "advance"),
                section,
                false);

            if (ledeWrite is null)
                ledeWrite = write with { SectionKey = "lede" };
            else
                sections.Add(write);

            bodySections.Add(section);
        }

        if (ledeWrite is null)
            throw new InvalidOperationException("Tool overview outline produced no sections.");

        var document = new ContentDocument(ledeWrite.Section, bodySections.Skip(1).ToList());
        OverviewMetadataDraft summaryMeta;
        try
        {
            var metaResult = await wc.Provider.CompleteAsync(
                _prompts.BuildOverviewMetadataPrompt(
                    wc.BaseContext,
                    metadata.Title,
                    document,
                    metadata.MetaDescription),
                ct);
            summaryMeta = LlmResponseJsonParser.Parse<OverviewMetadataDraft>(metaResult.Content, "tool overview metadata");
            tokens += (metaResult.PromptTokens ?? 0) + (metaResult.CompletionTokens ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool overview metadata generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        _ = ownerUserId;
        return new GccV2WriteOutput
        {
            Title = metadata.Title,
            MetaDescription = summaryMeta.MetaDescription,
            Lede = ledeWrite,
            Sections = sections,
            TokensUsed = tokens,
            ToolPage = new GccV2ToolPageWriteExtras(
                Kind: "overview",
                Slug: slug,
                JsonLdSchema: null,
                Keywords: [keyword],
                Excerpt: summaryMeta.Summary,
                MainSummary: summaryMeta.MainSummary,
                HeroSummary: summaryMeta.HeroSummary,
                HomeSummary: summaryMeta.HomeSummary,
                BlogSummary: summaryMeta.BlogSummary,
                AdvertisingSummary: summaryMeta.AdvertisingSummary,
                SourceAttributionHtml: null,
                PillarArticleUrl: pillar?.CanonicalUrl),
        };
    }

    private async Task<(Section Section, int Tokens)> DraftOverviewSectionAsync(
        GccV2WriteContext wc,
        ProjectGenerationContext sectionContext,
        ArticleMetadataDraft metadata,
        GccV2OutlineSection entry,
        int index,
        int totalCount,
        IReadOnlyList<string> allHeadings,
        string? pillarExcerpt,
        CancellationToken ct)
    {
        try
        {
            var result = await wc.Provider.CompleteAsync(
                _prompts.BuildOverviewSectionPrompt(
                    sectionContext,
                    metadata,
                    entry.Heading,
                    index,
                    totalCount,
                    allHeadings,
                    pillarExcerpt),
                ct);
            var section = LlmResponseJsonParser.ParseSection(
                result.Content, "h2", $"tool overview section \"{entry.Heading}\"");
            return (section, (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool overview section \"{Heading}\" failed for job {JobId}.", entry.Heading, wc.Job.Id);
            throw;
        }
    }

    private async Task<(Section Section, int Tokens)> BuildToolsIndexSectionAsync(
        GccV2WriteContext wc,
        ArticleMetadataDraft metadata,
        string sectionHeading,
        string toolsHeading,
        IReadOnlyList<PartnerResearchRow> partners,
        IReadOnlyList<string> allPartnerNames,
        CancellationToken ct)
    {
        var children = new List<Section>();
        var tokens = 0;
        for (var i = 0; i < partners.Count; i++)
        {
            var partner = partners[i];
            try
            {
                var (child, used) = await CompletePartnerChildSectionAsync(
                    wc, metadata, toolsHeading, partner, allPartnerNames, i, partners.Count, ct);
                children.Add(child with { Heading = partner.Name, Tag = "h3" });
                tokens += used;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tools index subsection \"{Name}\" failed for job {JobId}.", partner.Name, wc.Job.Id);
                throw;
            }
        }

        var toolsSection = new Section("h2", sectionHeading, [], null, children);
        var partnerLinks = partners.Select(p => (p.Name, p.OnSiteHref)).ToList();
        var injected = InjectOnSiteToolLinks([toolsSection], partnerLinks, toolsHeading).First();
        return (injected, tokens);
    }

    private async Task<(Section Section, int Tokens)> CompletePartnerChildSectionAsync(
        GccV2WriteContext wc,
        ArticleMetadataDraft metadata,
        string toolsHeading,
        PartnerResearchRow partner,
        IReadOnlyList<string> allPartnerNames,
        int index,
        int totalCount,
        CancellationToken ct)
    {
        ContentGenerationException? lastParseFailure = null;
        var tokens = 0;
        // One retry: models occasionally return truncated or non-hygienic JSON for a single partner.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await wc.Provider.CompleteAsync(
                _prompts.BuildOverviewPartnerChildPrompt(
                    wc.BaseContext,
                    metadata,
                    toolsHeading,
                    partner.Name,
                    allPartnerNames,
                    index,
                    totalCount,
                    partner.ResearchJson,
                    partner.OnSiteHref),
                ct);
            tokens += (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0);
            try
            {
                var child = LlmResponseJsonParser.ParseSection(
                    result.Content, "h3", $"tools index \"{partner.Name}\"");
                return (child, tokens);
            }
            catch (ContentGenerationException ex)
            {
                lastParseFailure = ex;
                _logger.LogWarning(
                    ex,
                    "Tools index subsection \"{Name}\" parse failed on attempt {Attempt} for job {JobId}.",
                    partner.Name,
                    attempt + 1,
                    wc.Job.Id);
            }
        }

        throw lastParseFailure
              ?? new ContentGenerationException($"Tools index \"{partner.Name}\" failed after retries.");
    }

    internal static string ResolveToolsHeading(IReadOnlyList<GccV2OutlineSection> outline, string keyword)
    {
        var match = outline.FirstOrDefault(s =>
            s.Heading.Contains("Tools for", StringComparison.OrdinalIgnoreCase));
        return match?.Heading ?? $"Tools for {keyword}";
    }

    internal static bool IsToolsIndexHeading(string heading, string toolsHeading) =>
        HeadingMatches(heading, toolsHeading);

    internal static List<Section> InjectOnSiteToolLinks(
        IReadOnlyList<Section> sections,
        IReadOnlyList<(string Name, string OnSiteHref)> partnerLinks,
        string toolsHeading)
    {
        if (partnerLinks.Count == 0) return sections.ToList();

        var result = new List<Section>();
        foreach (var section in sections)
        {
            if (!HeadingMatches(section.Heading, toolsHeading))
            {
                result.Add(section);
                continue;
            }

            var children = section.Children.ToList();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var partner in partnerLinks)
            {
                if (!used.Add(partner.Name)) continue;
                var existingIdx = children.FindIndex(c =>
                    string.Equals(c.Heading, partner.Name, StringComparison.OrdinalIgnoreCase));
                if (existingIdx >= 0)
                {
                    children[existingIdx] = children[existingIdx] with
                    {
                        Tag = "h3",
                        Href = partner.OnSiteHref,
                    };
                }
                else
                {
                    children.Add(new Section(
                        "h3",
                        partner.Name,
                        [new TextParagraph([new Run($"See {partner.OnSiteHref} for a full overview of {partner.Name}.")])],
                        partner.OnSiteHref,
                        []));
                }
            }

            result.Add(section with { Children = children });
        }

        return result;
    }

    private static bool HeadingMatches(string heading, string toolsHeading) =>
        string.Equals(heading.Trim(), toolsHeading.Trim(), StringComparison.OrdinalIgnoreCase)
        || heading.Contains("Tools for", StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<PartnerResearchRow>> LoadPartnerResearchAsync(Guid createId, CancellationToken ct)
    {
        var jobs = await _repo.ListJobsByCreateAsync(createId, ct);
        var rows = new List<PartnerResearchRow>();
        foreach (var job in jobs.Where(j => string.Equals(j.ContentType, "tool", StringComparison.OrdinalIgnoreCase)))
        {
            var brief = await _repo.GetBriefAsync(job.BriefId, ct);
            var target = GccV2ToolPageTargetParser.Parse(brief?.RawBriefJson);
            if (target is null || !target.IsPartner) continue;
            var href = target.OnSiteHref ?? GccV2ToolSlugHelper.OnSiteHref(target.Slug);
            var research = GccV2ToolResearchExtractor.DeserializeResearch(target.ExtractedResearch);
            var researchJson = research is null ? null : GccV2ToolResearchExtractor.SerializeResearch(research);
            rows.Add(new PartnerResearchRow(target.Name, href, researchJson));
        }

        return rows
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<PillarSnapshot?> ResolvePillarAsync(Guid createId, CancellationToken ct)
    {
        var jobs = await _repo.ListJobsByCreateAsync(createId, ct);
        var pillarJob = jobs.FirstOrDefault(j =>
            string.Equals(j.ContentType, "pillar", StringComparison.OrdinalIgnoreCase)
            && string.Equals(j.Status, "ready", StringComparison.OrdinalIgnoreCase));
        if (pillarJob is null || string.IsNullOrWhiteSpace(pillarJob.ResultJson)) return null;

        try
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new ParagraphJsonConverter());
            var payload = JsonSerializer.Deserialize<PillarResultPayload>(
                pillarJob.ResultJson,
                options);
            if (payload?.Document is null) return null;
            var title = payload.Title ?? "";
            var slug = SlugHelper.Slugify(title);
            var canonical = $"{_company.ArticleBaseUrl.TrimEnd('/')}/{GccV2ToolSlugHelper.DefaultDepartment}/{slug}";
            var excerpt = ContentDocumentText.Flatten(payload.Document);
            if (excerpt.Length > 2500) excerpt = excerpt[..2500] + "…";
            return new PillarSnapshot(title, payload.MetaDescription, canonical, excerpt);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    internal sealed record PartnerResearchRow(string Name, string OnSiteHref, string? ResearchJson);

    private sealed record PillarSnapshot(string? Title, string? MetaDescription, string CanonicalUrl, string Excerpt);

    private sealed record PillarResultPayload(string? Title, string? MetaDescription, ContentDocument? Document);

    private sealed record OverviewMetadataDraft(
        string Summary,
        string MainSummary,
        string HeroSummary,
        string HomeSummary,
        string BlogSummary,
        string AdvertisingSummary,
        string MetaDescription);
}
