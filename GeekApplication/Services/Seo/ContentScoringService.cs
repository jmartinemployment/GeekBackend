using System.Text.Json;
using GeekSeo.Persistence.Entities;
using GeekApplication.Infrastructure;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class ContentScoringService(
    IContentDocumentService documents,
    IContentDocumentRepository documentRepo,
    ISerpCacheRepository serpCache,
    ISerpProvider serpProvider,
    CompetitorCrawlService competitorCrawl,
    IRichTextProvider richText,
    IAIProvider ai) : IContentScoringService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public Task<Result<ContentScoreHubResult>> ProcessContentChangedAsync(
        Guid userId, Guid documentId, string contentHtml, string targetKeyword,
        CancellationToken ct = default) =>
        ScoreAsync(userId, documentId, contentHtml, targetKeyword, invalidateCache: false, ct);

    public async Task<Result<ContentScoreHubResult>> ProcessKeywordChangedAsync(
        Guid userId, Guid documentId, string contentHtml, string targetKeyword, string targetLocation,
        CancellationToken ct = default)
    {
        var access = await documents.EnsureAccessAsync(userId, documentId, ct);
        if (!access.IsSuccess)
            return Result<ContentScoreHubResult>.Failure(access.Error ?? "Access denied");

        await documents.UpdateContentAsync(
            userId,
            documentId,
            new UpdateContentRequest
            {
                ContentHtml = contentHtml,
                TargetKeyword = targetKeyword,
                TargetLocation = targetLocation,
            },
            ct);

        return await ScoreAsync(userId, documentId, contentHtml, targetKeyword, invalidateCache: true, ct);
    }

    public async Task<Result<AutoOptimizeResult>> AutoOptimizeAsync(
        Guid userId, Guid documentId, CancellationToken ct = default)
    {
        var access = await documents.EnsureAccessAsync(userId, documentId, ct);
        if (!access.IsSuccess || access.Value is null)
            return Result<AutoOptimizeResult>.Failure(access.Error ?? "Access denied");

        var doc = access.Value;
        var before = await ScoreAsync(userId, documentId, doc.ContentHtml, doc.TargetKeyword, invalidateCache: false, ct);
        if (!before.IsSuccess || before.Value?.ScoreUpdate is null)
            return Result<AutoOptimizeResult>.Failure(before.Error ?? "Could not score document before optimize");

        var previousScore = before.Value.ScoreUpdate.Score;
        var suggestions = before.Value.ScoreUpdate.Suggestions
            .Select(s => s.ActionText)
            .ToList();

        var optimized = await ai.CompleteAsync(new AIRequest
        {
            SystemPrompt =
                "You optimize SEO HTML articles. Apply the suggestions while preserving meaning. Return improved HTML only (h1/h2/p). No markdown fences.",
            UserPrompt =
                $"Keyword: {doc.TargetKeyword}\nCurrent score: {previousScore}/100\n\nSuggestions:\n- {string.Join("\n- ", suggestions)}\n\nHTML:\n{doc.ContentHtml}",
            MaxTokens = 8192,
            Temperature = 0.5,
        }, ct);

        if (!optimized.IsSuccess || optimized.Value is null)
            return Result<AutoOptimizeResult>.Failure(optimized.Error ?? "Auto-optimize failed");

        var patchedHtml = AiHtmlSanitizer.ToHtmlFragment(optimized.Value.Content);
        var after = await ScoreAsync(userId, documentId, patchedHtml, doc.TargetKeyword, invalidateCache: false, ct);
        var estimated = after.Value?.ScoreUpdate?.Score ?? previousScore;

        return Result<AutoOptimizeResult>.Success(new AutoOptimizeResult
        {
            ContentHtml = patchedHtml,
            PreviousScore = previousScore,
            EstimatedScore = estimated,
            ChangesApplied = suggestions.Take(5).ToList(),
            ScoreUpdate = after.Value?.ScoreUpdate,
        });
    }

    private async Task<Result<ContentScoreHubResult>> ScoreAsync(
        Guid userId,
        Guid documentId,
        string contentHtml,
        string targetKeyword,
        bool invalidateCache,
        CancellationToken ct)
    {
        var access = await documents.EnsureAccessAsync(userId, documentId, ct);
        if (!access.IsSuccess || access.Value is null)
            return Result<ContentScoreHubResult>.Failure(access.Error ?? "Access denied");

        var doc = access.Value;
        var keyword = string.IsNullOrWhiteSpace(targetKeyword) ? doc.TargetKeyword : targetKeyword;
        var location = string.IsNullOrWhiteSpace(doc.TargetLocation) ? "United States" : doc.TargetLocation;
        const string languageCode = "en";

        if (invalidateCache)
        {
            var deleted = await serpCache.DeleteAsync(keyword, location, languageCode, ct);
            if (!deleted.IsSuccess)
                return Result<ContentScoreHubResult>.Failure(deleted.Error ?? "Failed to invalidate SERP cache");
        }

        var benchmarkResult = await ResolveBenchmarksAsync(keyword, location, languageCode, ct);
        if (!benchmarkResult.IsSuccess)
            return Result<ContentScoreHubResult>.Failure(benchmarkResult.Error ?? "Benchmark error");
        if (benchmarkResult.Value?.PendingReason is not null)
            return Result<ContentScoreHubResult>.Success(new ContentScoreHubResult { PendingReason = benchmarkResult.Value.PendingReason });

        var benchmarks = benchmarkResult.Value!.Benchmarks;
        var serpFeatures = benchmarkResult.Value.SerpFeatures;

        var plainText = richText.ExtractPlainText(contentHtml);
        var wordCount = richText.CountWords(contentHtml);
        var titleTag = ExtractMeta(contentHtml, "title") ?? richText.ExtractPlainText(ExtractTag(contentHtml, "h1"));

        var termScore = ScoreTermCoverage(plainText, keyword);
        var wordScore = ScoreWordCount(wordCount, benchmarks.AvgWordCount);
        var titleScore = ScoreTitle(titleTag, keyword, benchmarks.AvgTitleLength);
        var headingScore = ScoreHeadings(contentHtml);
        var metaScore = ScoreMeta(ExtractMeta(contentHtml, "description"), keyword);
        var readabilityScore = ScoreReadability(plainText);

        var total = termScore + wordScore + headingScore + titleScore + metaScore + readabilityScore;
        var grade = ScoreToGrade(total);
        var suggestions = BuildSuggestions(wordCount, benchmarks.AvgWordCount, termScore, titleScore, keyword);

        var update = new ScoreUpdateMessage
        {
            Score = total,
            Grade = grade,
            Components = new
            {
                termCoverage = termScore,
                wordCount = wordScore,
                headingStructure = headingScore,
                titleTag = titleScore,
                metaDescription = metaScore,
                readability = readabilityScore,
            },
            Suggestions = suggestions,
            SerpFeatures = SerpFeatureGuidanceBuilder.Build(serpFeatures),
            EeatAdvisories = EeatAdvisoryBuilder.Build(plainText, contentHtml),
            BenchmarkQuality = benchmarks.BenchmarkQuality,
            Timestamp = DateTimeOffset.UtcNow,
        };

        var componentsJson = JsonSerializer.Serialize(update.Components, JsonOptions);
        await documentRepo.UpdateScoreAsync(documentId, total, componentsJson, ct);

        return Result<ContentScoreHubResult>.Success(new ContentScoreHubResult { ScoreUpdate = update });
    }

    private async Task<Result<BenchmarkResolution>> ResolveBenchmarksAsync(
        string keyword, string location, string languageCode, CancellationToken ct)
    {
        var cacheResult = await serpCache.GetAsync(keyword, location, languageCode, ct);
        if (!cacheResult.IsSuccess)
            return Result<BenchmarkResolution>.Failure(cacheResult.Error ?? "SERP cache error");

        SerpBenchmarksPayload? benchmarks = null;
        SerpFeatures? serpFeatures = null;
        SerpResult? serpData = null;
        SeoSerpResult? serpRow = cacheResult.Value;

        if (serpRow is null || serpRow.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            var fetch = await serpProvider.GetSerpResultsAsync(new SerpRequest
            {
                Keyword = keyword,
                Location = location,
                LanguageCode = languageCode,
                ResultCount = 10,
            }, ct);

            if (!fetch.IsSuccess || fetch.Value is null)
            {
                return Result<BenchmarkResolution>.Success(new BenchmarkResolution
                {
                    PendingReason = fetch.Error ?? "benchmark_refreshing",
                });
            }

            serpData = fetch.Value;
            benchmarks = SerpBenchmarkCalculator.FromSerp(serpData);
            serpFeatures = serpData.Features;
            var upserted = await serpCache.UpsertAsync(keyword, location, languageCode, serpData, benchmarks, ct);
            if (upserted.IsSuccess && upserted.Value is not null)
                serpRow = upserted.Value;
        }
        else
        {
            benchmarks = JsonSerializer.Deserialize<SerpBenchmarksPayload>(serpRow.ResultsJson, JsonOptions);
            serpFeatures = JsonSerializer.Deserialize<SerpFeatures>(serpRow.SerpFeaturesJson, JsonOptions);
            serpData = new SerpResult
            {
                Keyword = keyword,
                Location = location,
                OrganicResults = benchmarks?.OrganicResults ?? [],
                Features = serpFeatures ?? new SerpFeatures(),
                FetchedAt = serpRow.FetchedAt,
            };
        }

        benchmarks ??= new SerpBenchmarksPayload
        {
            AvgWordCount = 1200,
            AvgTitleLength = 55,
            BenchmarkQuality = "low_sample_count",
        };
        serpFeatures ??= new SerpFeatures();

        if (serpRow is not null && serpData is not null && serpData.OrganicResults.Count > 0)
        {
            var pages = await competitorCrawl.EnsureCompetitorPagesAsync(serpRow.Id, serpData.OrganicResults, ct);
            if (pages.IsSuccess && pages.Value is not null)
            {
                benchmarks = CompetitorCrawlService.BenchmarksFromCompetitors(serpData, pages.Value, benchmarks);
            }
        }

        return Result<BenchmarkResolution>.Success(new BenchmarkResolution
        {
            Benchmarks = benchmarks,
            SerpFeatures = serpFeatures,
        });
    }

    private sealed record BenchmarkResolution
    {
        public SerpBenchmarksPayload Benchmarks { get; init; } = new();
        public SerpFeatures SerpFeatures { get; init; } = new();
        public string? PendingReason { get; init; }
    }

    private static int ScoreReadability(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return 0;

        var words = plainText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words < 40)
            return 4;

        var sentences = Math.Max(1, plainText.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries).Length);
        var avgSentenceLength = words / (double)sentences;

        return avgSentenceLength switch
        {
            >= 12 and <= 22 => 10,
            >= 8 and <= 28 => 7,
            _ => 4,
        };
    }

    private static int ScoreTermCoverage(string plainText, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return 0;
        var lower = plainText.ToLowerInvariant();
        var terms = keyword.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = terms.Count(t => lower.Contains(t, StringComparison.Ordinal));
        return (int)Math.Round((double)found / terms.Length * 35);
    }

    private static int ScoreWordCount(int wordCount, int target) =>
        wordCount >= target
            ? 20
            : (int)Math.Round(Math.Min(1.0, wordCount / (double)Math.Max(target, 1)) * 20);

    private static int ScoreTitle(string? title, string keyword, int avgLength)
    {
        if (string.IsNullOrWhiteSpace(title))
            return 0;
        var score = 0;
        if (title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            score += 6;
        if (title.Length is >= 30 and <= 65)
            score += 4;
        else if (Math.Abs(title.Length - avgLength) < 20)
            score += 2;
        return Math.Min(10, score);
    }

    private static int ScoreHeadings(string html)
    {
        var h2Count = CountOccurrences(html, "<h2");
        return h2Count switch
        {
            >= 3 and <= 8 => 15,
            2 => 10,
            1 => 6,
            _ => 0,
        };
    }

    private static int ScoreMeta(string? meta, string keyword)
    {
        if (string.IsNullOrWhiteSpace(meta))
            return 0;
        var score = meta.Length is >= 120 and <= 160 ? 5 : 2;
        if (meta.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            score += 5;
        return Math.Min(10, score);
    }

    private static List<ScoreSuggestion> BuildSuggestions(
        int wordCount, int targetWords, int termScore, int titleScore, string keyword)
    {
        var list = new List<ScoreSuggestion>();
        if (wordCount < targetWords)
        {
            list.Add(new ScoreSuggestion
            {
                Component = "wordCount",
                PointValue = 20 - ScoreWordCount(wordCount, targetWords),
                ActionText = $"Add about {targetWords - wordCount} words to match competitor length (~{targetWords}).",
            });
        }
        if (termScore < 30)
        {
            list.Add(new ScoreSuggestion
            {
                Component = "termCoverage",
                PointValue = 35 - termScore,
                ActionText = $"Use the phrase \"{keyword}\" and related terms more naturally in the body.",
            });
        }
        if (titleScore < 8)
        {
            list.Add(new ScoreSuggestion
            {
                Component = "titleTag",
                PointValue = 10 - titleScore,
                ActionText = "Include the target keyword in the title and keep length near 55 characters.",
            });
        }
        return list.OrderByDescending(s => s.PointValue).ToList();
    }

    private static string ScoreToGrade(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 60 => "D",
        _ => "F",
    };

    private static int CountOccurrences(string haystack, string needle) =>
        haystack.Split(needle, StringSplitOptions.None).Length - 1;

    private static string? ExtractMeta(string html, string name)
    {
        var marker = $"name=\"{name}\"";
        var idx = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var contentIdx = html.IndexOf("content=\"", idx, StringComparison.OrdinalIgnoreCase);
        if (contentIdx < 0)
            return null;
        var start = contentIdx + 9;
        var end = html.IndexOf('"', start);
        return end > start ? html[start..end] : null;
    }

    private static string ExtractTag(string html, string tag)
    {
        var open = $"<{tag}";
        var idx = html.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return string.Empty;
        var closeStart = html.IndexOf('>', idx);
        var closeEnd = html.IndexOf($"</{tag}>", closeStart, StringComparison.OrdinalIgnoreCase);
        return closeEnd > closeStart ? html[(closeStart + 1)..closeEnd] : string.Empty;
    }
}
