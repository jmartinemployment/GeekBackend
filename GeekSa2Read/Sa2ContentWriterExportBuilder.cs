using GeekSeo.Application.Models.Seo;

namespace GeekSa2Read;

/// <summary>
/// Builds <see cref="ContentWriterSerpExport"/> from sa2 rows (mirrors Site Analyzer export contract).
/// </summary>
internal static class Sa2ContentWriterExportBuilder
{
    private const string Organic = "organic";
    private const string AiOverview = "ai_overview";
    private const string RelatedSearches = "related_searches";
    private const string PeopleAlsoAsk = "people_also_ask";

    public static ContentWriterSerpExport Build(
        AnalysisRunRow run,
        IReadOnlyList<SerpItemRow> serpItems,
        IReadOnlyList<RelatedQueryRow> relatedQueries,
        IReadOnlyList<CompetitorPageRow> competitorPages,
        IReadOnlyList<CompetitorHeadingRow> competitorHeadings,
        IReadOnlyList<CompetitorJsonLdRow> competitorJsonLd,
        IReadOnlyList<SourcePageRow> sourcePages,
        IReadOnlyList<PageHeadingRow> pageHeadings,
        IReadOnlyList<string> authorityPageUrls,
        Guid? geekSeoProjectId,
        DateTimeOffset capturedAt)
    {
        var relatedByItem = relatedQueries
            .GroupBy(q => q.SerpItemId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Sequence).ToList());

        var headingsByPage = competitorHeadings
            .GroupBy(h => h.CompetitorPageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var jsonLdByPage = competitorJsonLd
            .GroupBy(j => j.CompetitorPageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var headingsBySourcePage = pageHeadings
            .GroupBy(h => h.PageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var gapTopics = Sa2Json.ParseStringList(run.GapTopics);
        var serp = BuildSerpItems(serpItems, relatedByItem);
        var competitors = BuildCompetitors(competitorPages, headingsByPage, jsonLdByPage);
        var sourceHeadings = BuildSourceHeadings(run, sourcePages, headingsBySourcePage);

        return new ContentWriterSerpExport
        {
            BundleVersion = ContentWriterSerpExport.CurrentBundleVersion,
            CapturedAt = capturedAt,
            RunId = run.Id,
            ProjectId = run.ProjectId,
            GeekSeoProjectId = geekSeoProjectId,
            Keyword = run.Keyword,
            TargetSiteUrl = run.TargetSiteUrl,
            Status = run.Status,
            SerpSeResultsCount = run.SerpSeResultsCount ?? 0,
            SerpCapturedAt = ToOffset(run.SerpCapturedAt),
            CompetitorCrawlStatus = run.CompetitorCrawlStatus,
            CompetitorCrawlFinishedAt = ToOffset(run.CompetitorCrawlFinishedAt),
            MatchedPillarTopic = run.MatchedPillarTopic,
            MatchedPillarIntent = run.MatchedPillarIntent,
            MatchedPillarAngle = run.MatchedPillarAngle,
            GapTopics = gapTopics,
            WritingInstructions = run.WritingInstructions,
            WritingRecommendations = BuildKeywordWritingRecommendations(run, gapTopics),
            Serp = serp,
            SourceHeadings = sourceHeadings,
            Competitors = competitors,
            Benchmarks = BuildBenchmarks(competitorPages, competitors),
            CitationCandidates = BuildCitationCandidates(serpItems, authorityPageUrls),
        };
    }

    private static List<ContentWriterSerpItem> BuildSerpItems(
        IReadOnlyList<SerpItemRow> items,
        IReadOnlyDictionary<Guid, List<RelatedQueryRow>> relatedByItem)
    {
        var serp = new List<ContentWriterSerpItem>();
        var position = 1;

        foreach (var item in items.OrderBy(i => i.RankAbsolute))
        {
            relatedByItem.TryGetValue(item.Id, out var related);

            if (string.Equals(item.Type, Organic, StringComparison.OrdinalIgnoreCase) && !item.Ads)
            {
                serp.Add(new ContentWriterSerpItem
                {
                    Position = item.RankGroup > 0 ? item.RankGroup : position++,
                    Type = Organic,
                    Title = item.Title,
                    Url = item.Url,
                    Domain = item.Domain,
                    Snippet = BuildSnippet(item),
                    Date = item.PreSnippet,
                    SiteName = item.WebsiteName,
                });
                continue;
            }

            if (string.Equals(item.Type, AiOverview, StringComparison.OrdinalIgnoreCase))
            {
                serp.Add(new ContentWriterSerpItem
                {
                    Position = position++,
                    Type = AiOverview,
                    Snippet = item.AiOverviewMarkdown
                        ?? item.AiOverviewStatusMessage
                        ?? item.Description,
                });
                continue;
            }

            if (string.Equals(item.Type, RelatedSearches, StringComparison.OrdinalIgnoreCase))
            {
                var queries = (related ?? [])
                    .Select(q => q.QueryText)
                    .Where(q => !string.IsNullOrWhiteSpace(q))
                    .ToList();
                if (queries.Count > 0)
                {
                    serp.Add(new ContentWriterSerpItem
                    {
                        Position = position++,
                        Type = RelatedSearches,
                        RelatedQuestions = queries,
                    });
                }

                continue;
            }

            if ((related?.Count ?? 0) > 0
                && (string.Equals(item.Type, PeopleAlsoAsk, StringComparison.OrdinalIgnoreCase)
                    || related!.Any(q => string.Equals(q.QueryType, "PeopleAlsoAsk", StringComparison.OrdinalIgnoreCase))))
            {
                serp.Add(new ContentWriterSerpItem
                {
                    Position = position++,
                    Type = PeopleAlsoAsk,
                    RelatedQuestions = related!
                        .Select(q => q.QueryText)
                        .Where(q => !string.IsNullOrWhiteSpace(q))
                        .ToList(),
                    Snippet = item.Description,
                });
            }
        }

        return serp;
    }

    private static List<ContentWriterCompetitorExport> BuildCompetitors(
        IReadOnlyList<CompetitorPageRow> competitorPages,
        IReadOnlyDictionary<Guid, List<CompetitorHeadingRow>> headingsByPage,
        IReadOnlyDictionary<Guid, List<CompetitorJsonLdRow>> jsonLdByPage)
    {
        if (competitorPages.Count == 0)
            return [];

        var pagesPerDomain = competitorPages
            .GroupBy(p => p.Domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return competitorPages
            .Where(p => p.DepthFromSeed == 0)
            .GroupBy(p => p.Domain, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(p => p.SeedRankAbsolute).First())
            .OrderBy(p => p.SeedRankAbsolute)
            .Select(page =>
            {
                headingsByPage.TryGetValue(page.Id, out var headings);
                jsonLdByPage.TryGetValue(page.Id, out var jsonLd);
                var mappedHeadings = MapCompetitorHeadings(headings ?? []);
                var schemaTypes = ExtractSchemaTypes(jsonLd ?? []);
                return new ContentWriterCompetitorExport
                {
                    Domain = page.Domain,
                    Url = page.Url,
                    SeedRankAbsolute = page.SeedRankAbsolute,
                    PagesCrawledOnDomain = pagesPerDomain.GetValueOrDefault(page.Domain, 1),
                    Headings = mappedHeadings,
                    WordCountEstimate = EstimateWordCountFromHeadings(mappedHeadings),
                    SchemaTypes = schemaTypes,
                    HasFaqSchema = schemaTypes.Any(t =>
                        string.Equals(t, "FAQPage", StringComparison.OrdinalIgnoreCase)),
                };
            })
            .ToList();
    }

    private static List<ContentWriterHeading> BuildSourceHeadings(
        AnalysisRunRow run,
        IReadOnlyList<SourcePageRow> sourcePages,
        IReadOnlyDictionary<Guid, List<PageHeadingRow>> headingsByPage)
    {
        if (sourcePages.Count == 0)
            return [];

        var targetPage = sourcePages.FirstOrDefault(p => UrlsMatch(p.Url, run.TargetSiteUrl))
            ?? sourcePages.OrderBy(p => p.DepthFromHomepage ?? int.MaxValue).FirstOrDefault();

        if (targetPage is null)
            return [];

        headingsByPage.TryGetValue(targetPage.Id, out var headings);
        return MapPageHeadings(headings ?? []);
    }

    private static ContentWriterExportBenchmarks BuildBenchmarks(
        IReadOnlyList<CompetitorPageRow> competitorPages,
        IReadOnlyList<ContentWriterCompetitorExport> seedCompetitors)
    {
        var topFive = seedCompetitors.Take(5).ToList();
        var h2Counts = topFive.Select(c => c.Headings.Count(h => h.Level == 2)).ToList();
        var wordCounts = topFive.Select(c => c.WordCountEstimate).Where(c => c > 0).ToList();

        return new ContentWriterExportBenchmarks
        {
            MedianH2CountTop5 = Median(h2Counts),
            MedianWordCountTop5 = Median(wordCounts),
            CompetitorDomainCount = competitorPages
                .Select(p => p.Domain)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            CompetitorPageCount = competitorPages.Count,
        };
    }

    private static List<ContentWriterCitationCandidate> BuildCitationCandidates(
        IReadOnlyList<SerpItemRow> serpItems,
        IReadOnlyList<string> authorityPageUrls)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<ContentWriterCitationCandidate>();

        foreach (var item in serpItems
                     .Where(i => string.Equals(i.Type, Organic, StringComparison.OrdinalIgnoreCase) && !i.Ads)
                     .OrderBy(i => i.RankGroup > 0 ? i.RankGroup : i.RankAbsolute)
                     .Take(10))
        {
            if (string.IsNullOrWhiteSpace(item.Url) || !seen.Add(item.Url.Trim()))
                continue;

            candidates.Add(new ContentWriterCitationCandidate
            {
                Url = item.Url.Trim(),
                Title = item.Title,
                Domain = item.Domain,
                Source = "organic",
            });
        }

        foreach (var url in authorityPageUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Take(8))
        {
            var trimmed = url.Trim();
            if (!seen.Add(trimmed))
                continue;

            candidates.Add(new ContentWriterCitationCandidate
            {
                Url = trimmed,
                Source = "authority",
            });
        }

        return candidates;
    }

    private static List<string> BuildKeywordWritingRecommendations(AnalysisRunRow run, IReadOnlyList<string> gapTopics)
    {
        var recommendations = new List<string>();

        if (!string.IsNullOrWhiteSpace(run.MatchedPillarTopic))
        {
            var line = $"Keyword \"{run.Keyword.Trim()}\": align with pillar \"{run.MatchedPillarTopic.Trim()}\"";
            if (!string.IsNullOrWhiteSpace(run.MatchedPillarIntent))
                line += $" ({run.MatchedPillarIntent.Trim()} intent)";
            if (!string.IsNullOrWhiteSpace(run.MatchedPillarAngle))
                line += $". Angle: {run.MatchedPillarAngle.Trim()}";
            recommendations.Add(line + ".");
        }

        if (gapTopics.Count > 0)
        {
            recommendations.Add(
                $"Content gaps for \"{run.Keyword.Trim()}\": {string.Join(", ", gapTopics.Take(5))}.");
        }

        return recommendations;
    }

    private static List<ContentWriterHeading> MapPageHeadings(IEnumerable<PageHeadingRow> headings) =>
        headings
            .OrderBy(h => h.Sequence)
            .Select(h => new ContentWriterHeading
            {
                Level = h.Level,
                Text = h.Text,
                Sequence = h.Sequence,
            })
            .ToList();

    private static List<ContentWriterHeading> MapCompetitorHeadings(IEnumerable<CompetitorHeadingRow> headings) =>
        headings
            .OrderBy(h => h.Sequence)
            .Select(h => new ContentWriterHeading
            {
                Level = h.Level,
                Text = h.Text,
                Sequence = h.Sequence,
            })
            .ToList();

    private static List<string> ExtractSchemaTypes(IEnumerable<CompetitorJsonLdRow> blocks) =>
        blocks
            .Select(b => b.ParsedType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int EstimateWordCountFromHeadings(IReadOnlyList<ContentWriterHeading> headings)
    {
        var text = string.Join(
            ' ',
            headings
                .OrderBy(h => h.Sequence)
                .Select(h => h.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t)));

        return string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static int Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    private static bool UrlsMatch(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(NormalizeUrl(left), NormalizeUrl(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return url.Trim().TrimEnd('/');

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    private static string? BuildSnippet(SerpItemRow item)
    {
        if (!string.IsNullOrWhiteSpace(item.Description))
            return item.Description;
        if (!string.IsNullOrWhiteSpace(item.ExtendedSnippet))
            return item.ExtendedSnippet;
        return item.PreSnippet;
    }

    internal sealed class AnalysisRunRow
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public string Keyword { get; init; } = string.Empty;
        public string TargetSiteUrl { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public long? SerpSeResultsCount { get; init; }
        public DateTime? SerpCapturedAt { get; init; }
        public string? CompetitorCrawlStatus { get; init; }
        public DateTime? CompetitorCrawlFinishedAt { get; init; }
        public string? MatchedPillarTopic { get; init; }
        public string? MatchedPillarIntent { get; init; }
        public string? MatchedPillarAngle { get; init; }
        public string? GapTopics { get; init; }
        public string? WritingInstructions { get; init; }
    }

    internal sealed class SerpItemRow
    {
        public Guid Id { get; init; }
        public string Type { get; init; } = string.Empty;
        public bool Ads { get; init; }
        public int RankAbsolute { get; init; }
        public int RankGroup { get; init; }
        public string? Title { get; init; }
        public string? Url { get; init; }
        public string? Domain { get; init; }
        public string? Description { get; init; }
        public string? ExtendedSnippet { get; init; }
        public string? PreSnippet { get; init; }
        public string? WebsiteName { get; init; }
        public string? AiOverviewMarkdown { get; init; }
        public string? AiOverviewStatusMessage { get; init; }
    }

    internal sealed class RelatedQueryRow
    {
        public Guid SerpItemId { get; init; }
        public string QueryText { get; init; } = string.Empty;
        public string QueryType { get; init; } = string.Empty;
        public int Sequence { get; init; }
    }

    internal sealed class CompetitorPageRow
    {
        public Guid Id { get; init; }
        public string Domain { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public int SeedRankAbsolute { get; init; }
        public int? DepthFromSeed { get; init; }
    }

    internal sealed class CompetitorHeadingRow
    {
        public Guid CompetitorPageId { get; init; }
        public int Level { get; init; }
        public string Text { get; init; } = string.Empty;
        public int Sequence { get; init; }
    }

    internal sealed class CompetitorJsonLdRow
    {
        public Guid CompetitorPageId { get; init; }
        public string? ParsedType { get; init; }
    }

    internal sealed class SourcePageRow
    {
        public Guid Id { get; init; }
        public string Url { get; init; } = string.Empty;
        public int? DepthFromHomepage { get; init; }
    }

    internal sealed class PageHeadingRow
    {
        public Guid PageId { get; init; }
        public int Level { get; init; }
        public string Text { get; init; } = string.Empty;
        public int Sequence { get; init; }
    }
}
