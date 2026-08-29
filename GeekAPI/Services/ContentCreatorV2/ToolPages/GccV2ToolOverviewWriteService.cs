using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

public sealed class GccV2ToolOverviewWriteService
{
    private readonly HttpGccV2Repository _repo;
    private readonly GccV2ToolPagePromptBuilder _prompts;
    private readonly GccV2ToolPageSpawnService _spawn;
    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccV2ToolOverviewWriteService> _logger;

    public GccV2ToolOverviewWriteService(
        HttpGccV2Repository repo,
        GccV2ToolPagePromptBuilder prompts,
        GccV2ToolPageSpawnService spawn,
        IOptions<CompanyProfileOptions> company,
        ILogger<GccV2ToolOverviewWriteService> logger)
    {
        _repo = repo;
        _prompts = prompts;
        _spawn = spawn;
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

        var partnerLinks = await LoadPartnerLinksAsync(wc.Job.CreateId, ct);
        if (partnerLinks.Count == 0)
        {
            _logger.LogWarning(
                "No partner tool jobs found for overview job {JobId} — writing keyword-only fallback.",
                wc.Job.Id);
        }

        var keyword = wc.BaseContext.TargetKeyword;
        var slug = string.IsNullOrWhiteSpace(target.Slug)
            ? GccV2ToolSlugHelper.SlugifyKeyword(keyword)
            : target.Slug;
        var toolsHeading = $"Tools for {keyword}";
        var metadata = new ArticleMetadataDraft(
            $"Tools for {keyword}",
            pillar?.MetaDescription ?? $"Overview of tools and capabilities for {keyword}.",
            [keyword],
            ["Overview", "Capabilities", "Implementation", "When to Use", toolsHeading]);

        var tokens = 0;
        List<Section> parsedSections;
        try
        {
            var bodyResult = await wc.Provider.CompleteAsync(
                _prompts.BuildOverviewBodyPrompt(
                    wc.BaseContext,
                    metadata,
                    toolsHeading,
                    partnerLinks,
                    pillar?.Excerpt),
                ct);
            parsedSections = LlmResponseJsonParser.ParseSections(bodyResult.Content, "tool overview body").ToList();
            tokens += (bodyResult.PromptTokens ?? 0) + (bodyResult.CompletionTokens ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool overview body generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        if (parsedSections.Count == 0)
            throw new InvalidOperationException("Tool overview body generation returned no sections.");

        parsedSections = InjectOnSiteToolLinks(parsedSections, partnerLinks, toolsHeading);

        var ledeSection = parsedSections[0] with { Tag = "h2" };
        var ledeWrite = new GccV2WriteSection("lede", ledeSection.Heading, "problem", ledeSection, false);
        var sections = new List<GccV2WriteSection>();
        for (var i = 1; i < parsedSections.Count; i++)
        {
            var section = parsedSections[i] with { Tag = "h2" };
            var key = SlugHelper.Slugify(section.Heading);
            if (string.IsNullOrWhiteSpace(key)) key = $"section-{i}";
            sections.Add(new GccV2WriteSection(key, section.Heading, "advance", section, false));
        }

        var document = new ContentDocument(ledeSection, sections.Select(s => s.Section).ToList());
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

    private async Task<IReadOnlyList<(string Name, string OnSiteHref)>> LoadPartnerLinksAsync(Guid createId, CancellationToken ct)
    {
        var jobs = await _repo.ListJobsByCreateAsync(createId, ct);
        var links = new List<(string Name, string OnSiteHref)>();
        foreach (var job in jobs.Where(j => string.Equals(j.ContentType, "tool", StringComparison.OrdinalIgnoreCase)))
        {
            var brief = await _repo.GetBriefAsync(job.BriefId, ct);
            var target = GccV2ToolPageTargetParser.Parse(brief?.RawBriefJson);
            if (target is null || !target.IsPartner) continue;
            var href = target.OnSiteHref ?? GccV2ToolSlugHelper.OnSiteHref(target.Slug);
            links.Add((target.Name, href));
        }

        return links
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
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
            var payload = JsonSerializer.Deserialize<PillarResultPayload>(
                pillarJob.ResultJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
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
    }

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
