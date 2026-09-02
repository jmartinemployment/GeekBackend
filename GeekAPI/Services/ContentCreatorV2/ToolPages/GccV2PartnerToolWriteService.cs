using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

public sealed class GccV2PartnerToolWriteService
{
    private readonly HttpGccV2Repository _repo;
    private readonly GccV2ToolPagePromptBuilder _prompts;
    private readonly GccV2ToolResearchExtractor _extractor;
    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccV2PartnerToolWriteService> _logger;

    public GccV2PartnerToolWriteService(
        HttpGccV2Repository repo,
        GccV2ToolPagePromptBuilder prompts,
        GccV2ToolResearchExtractor extractor,
        IOptions<CompanyProfileOptions> company,
        ILogger<GccV2PartnerToolWriteService> logger)
    {
        _repo = repo;
        _prompts = prompts;
        _extractor = extractor;
        _company = company.Value;
        _logger = logger;
    }

    public async Task<GccV2WriteOutput> WriteAsync(
        GccV2WriteContext wc,
        Guid ownerUserId,
        GccV2ToolPageTarget target,
        CancellationToken ct)
    {
        var toolName = string.IsNullOrWhiteSpace(target.Name) ? wc.BaseContext.TargetKeyword : target.Name.Trim();
        var slug = string.IsNullOrWhiteSpace(target.Slug) ? GccV2ToolSlugHelper.SlugifyToolName(toolName) : target.Slug;
        var sourceUrl = target.SourceUrl;
        var research = GccV2ToolResearchExtractor.DeserializeResearch(target.ExtractedResearch)
            ?? await _extractor.ExtractAsync(
                wc.Provider,
                toolName,
                sourceUrl,
                ParsePartnerResearchPages(wc.Brief.RawBriefJson),
                ct);
        var researchJson = research is null ? null : GccV2ToolResearchExtractor.SerializeResearch(research);

        var pillar = await ResolvePillarAsync(wc.Job.CreateId, ct);
        var pillarMetadata = new ArticleMetadataDraft(
            pillar?.Title ?? wc.BaseContext.TargetKeyword,
            pillar?.MetaDescription ?? "",
            [wc.BaseContext.TargetKeyword],
            []);
        var pillarExcerpt = pillar?.Excerpt;

        var app = new SoftwareApplicationDescriptor(toolName, research?.Summary, null);
        var tokens = 0;
        var headings = GccV2ToolPagePromptBuilder.PartnerSectionHeadings;
        var parsedSections = new List<Section>();

        for (var i = 0; i < headings.Length; i++)
        {
            var heading = headings[i]!;
            try
            {
                var bodyResult = await wc.Provider.CompleteAsync(
                    _prompts.BuildPartnerToolSectionPrompt(
                        wc.BaseContext,
                        pillarMetadata,
                        app,
                        slug,
                        heading,
                        i,
                        headings.Length,
                        researchJson,
                        pillarExcerpt),
                    ct);
                var section = LlmResponseJsonParser.ParseSection(
                    bodyResult.Content, "h2", $"partner tool section \"{heading}\"");
                parsedSections.Add(section with { Heading = heading, Tag = "h2" });
                tokens += (bodyResult.PromptTokens ?? 0) + (bodyResult.CompletionTokens ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Partner tool section \"{Heading}\" failed for job {JobId}.", heading, wc.Job.Id);
                throw;
            }
        }

        if (parsedSections.Count == 0)
            throw new InvalidOperationException("Partner tool body generation returned no sections.");

        var ledeSection = parsedSections[0];
        var ledeWrite = new GccV2WriteSection("lede", ledeSection.Heading, "problem", ledeSection, false);

        var sections = new List<GccV2WriteSection>();
        for (var i = 1; i < parsedSections.Count; i++)
        {
            var section = parsedSections[i];
            var key = SlugHelper.Slugify(section.Heading);
            if (string.IsNullOrWhiteSpace(key)) key = $"section-{i}";
            sections.Add(new GccV2WriteSection(key, section.Heading, "advance", section, false));
        }

        var document = new ContentDocument(ledeSection, sections.Select(s => s.Section).ToList());
        GccV2ToolMetadataDraft metadata;
        try
        {
            var metaResult = await wc.Provider.CompleteAsync(
                _prompts.BuildPartnerToolMetadataPrompt(wc.BaseContext, pillarMetadata, app, document), ct);
            metadata = LlmResponseJsonParser.Parse<GccV2ToolMetadataDraft>(metaResult.Content, "partner tool metadata");
            tokens += (metaResult.PromptTokens ?? 0) + (metaResult.CompletionTokens ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Partner tool metadata generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        var toolUrl = $"{wc.BaseContext.ToolBaseUrl.TrimEnd('/')}/{GccV2ToolSlugHelper.DefaultDepartment}/{slug}";
        var pillarArticleUrl = pillar?.CanonicalUrl ?? "";
        var partnerResearchPages = ParsePartnerResearchPages(wc.Brief.RawBriefJson);
        var attributionQuote = await BuildAttributionQuoteAsync(
            wc, toolName, sourceUrl, research, partnerResearchPages, ct);
        tokens += attributionQuote.Tokens;
        var sourceAttributionHtml = RequireSourceAttributionHtml(sourceUrl, attributionQuote.Text, toolName);

        _ = ownerUserId;
        var now = DateTime.UtcNow;
        var schemaMeta = new ContentMetadata(
            toolName,
            metadata.MetaDescription,
            _company.AuthorName,
            _company.PublisherName,
            _company.PublisherLogoUrl,
            toolUrl,
            _company.PublisherLogoUrl,
            now,
            now,
            [wc.BaseContext.TargetKeyword],
            ContentDocumentText.CountWords(document));

        var jsonLd = GccV2ToolPageSchemaBuilder.BuildToolPage(
            schemaMeta,
            pillarArticleUrl,
            app with { Url = toolUrl });

        return new GccV2WriteOutput
        {
            Title = toolName,
            MetaDescription = metadata.MetaDescription,
            Lede = ledeWrite,
            Sections = sections,
            TokensUsed = tokens,
            ToolPage = new GccV2ToolPageWriteExtras(
                Kind: "partner",
                Slug: slug,
                JsonLdSchema: jsonLd,
                Keywords: [wc.BaseContext.TargetKeyword],
                Excerpt: metadata.Summary,
                MainSummary: metadata.MainSummary,
                HeroSummary: metadata.HeroSummary,
                HomeSummary: metadata.HomeSummary,
                BlogSummary: metadata.BlogSummary,
                AdvertisingSummary: metadata.AdvertisingSummary,
                SourceAttributionHtml: sourceAttributionHtml,
                PillarArticleUrl: pillarArticleUrl),
        };
    }

    internal static string? BuildSourceAttributionHtml(string? sourceUrl, string quoteText, string toolName)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl)) return null;
        var text = GccV2ToolResearchExtractor.StripWrappingQuotes(quoteText);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return GccV2ToolSectionRenderer.RenderSourceAttribution(sourceUrl, text, toolName);
    }

    internal static string RequireSourceAttributionHtml(string? sourceUrl, string quoteText, string toolName)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return "";

        var html = BuildSourceAttributionHtml(sourceUrl, quoteText, toolName);
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new ContentGenerationException(
                $"Partner tool page for {toolName.Trim()} requires a verbatim source blockquote but none could be resolved from {sourceUrl}.");
        }

        if (!html.Contains("<blockquote", StringComparison.OrdinalIgnoreCase))
        {
            throw new ContentGenerationException(
                $"Partner tool page for {toolName.Trim()} is missing required source blockquote markup.");
        }

        return html;
    }

    private async Task<(string Text, int Tokens)> BuildAttributionQuoteAsync(
        GccV2WriteContext wc,
        string toolName,
        string? sourceUrl,
        GccV2ExtractedToolResearch? research,
        IReadOnlyList<GccQuoteablePage> partnerResearchFromBrief,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl)) return ("", 0);

        var partnerResearch = partnerResearchFromBrief.Count > 0
            ? partnerResearchFromBrief
            : await LoadPartnerResearchForCreateAsync(wc.Job.CreateId, ct);

        var page = ResolvePartnerPage(sourceUrl, partnerResearch);
        var pageText = GccV2ToolResearchExtractor.FormatPageText(page);
        var quote = GccV2ToolResearchExtractor.ResolveAttributionQuote(
            sourceUrl,
            partnerResearch,
            research?.SourceQuote,
            pageText);

        if (!string.IsNullOrWhiteSpace(quote))
            return (quote, 0);

        if (string.IsNullOrWhiteSpace(pageText))
        {
            throw new ContentGenerationException(
                $"Partner tool page for {toolName.Trim()} requires a verbatim source blockquote but no research text was found for {sourceUrl}.");
        }

        try
        {
            var result = await wc.Provider.CompleteAsync(
                _prompts.BuildSourceQuotePrompt(toolName, sourceUrl, pageText), ct);
            var text = GccV2ToolResearchExtractor.StripWrappingQuotes((result.Content ?? "").Trim());
            if (GccV2ToolResearchExtractor.IsMinimalVerbatimQuote(text)
                && GccV2ToolResearchExtractor.IsVerbatimFromPage(text, pageText))
            {
                return (text, (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0));
            }
        }
        catch (ContentGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Verbatim source quote selection failed for {Tool}.", toolName);
        }

        quote = page is null ? "" : GccV2ToolResearchExtractor.PickBestVerbatimQuote(page);
        if (!string.IsNullOrWhiteSpace(quote))
            return (quote, 0);

        throw new ContentGenerationException(
            $"Partner tool page for {toolName.Trim()} requires a verbatim source blockquote but none could be extracted from {sourceUrl}.");
    }

    private async Task<IReadOnlyList<GccQuoteablePage>> LoadPartnerResearchForCreateAsync(Guid createId, CancellationToken ct)
    {
        var jobs = await _repo.ListJobsByCreateAsync(createId, ct);
        foreach (var job in jobs)
        {
            var brief = await _repo.GetBriefAsync(job.BriefId, ct);
            var pages = ParsePartnerResearchPages(brief?.RawBriefJson);
            if (pages.Count > 0) return pages;
        }

        return [];
    }

    private static GccQuoteablePage? ResolvePartnerPage(string? sourceUrl, IReadOnlyList<GccQuoteablePage> pages)
    {
        if (pages.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            var match = pages.FirstOrDefault(p =>
                string.Equals(p.Url, sourceUrl, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return pages[0];
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

    private sealed record PillarSnapshot(string? Title, string? MetaDescription, string CanonicalUrl, string Excerpt);

    private sealed record PillarResultPayload(string? Title, string? MetaDescription, ContentDocument? Document);

    private static IReadOnlyList<GccQuoteablePage> ParsePartnerResearchPages(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("partnerResearch", out var el)
                || el.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<GccQuoteablePage>>(
                el.GetRawText(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
