using System.Text.Json;
using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class ContentBriefService(
    IProjectRepository projects,
    ISerpCacheRepository serpCache,
    ISerpProvider serpProvider,
    IAIProvider ai) : IContentBriefService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<Result<ContentBrief>> GenerateBriefAsync(
        Guid userId, GenerateBriefRequest request, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(request.ProjectId, ct);
        if (!project.IsSuccess || project.Value is null || project.Value.UserId != userId)
            return Result<ContentBrief>.Failure("Access denied");

        var keyword = request.Keyword.Trim();
        var location = string.IsNullOrWhiteSpace(request.Location)
            ? project.Value.DefaultLocation
            : request.Location;
        const string languageCode = "en";

        var serpRow = await EnsureSerpAsync(keyword, location, languageCode, ct);
        if (!serpRow.IsSuccess)
            return Result<ContentBrief>.Failure(serpRow.Error ?? "SERP error");
        if (serpRow.Value is null)
            return Result<ContentBrief>.Failure("Could not load SERP data for this keyword");

        var benchmarks = JsonSerializer.Deserialize<SerpBenchmarksPayload>(serpRow.Value.ResultsJson, JsonOptions)
            ?? new SerpBenchmarksPayload();
        var paa = JsonSerializer.Deserialize<List<PeopleAlsoAskResult>>(serpRow.Value.PeopleAlsoAskJson, JsonOptions) ?? [];
        var related = JsonSerializer.Deserialize<List<string>>(serpRow.Value.RelatedSearchesJson, JsonOptions) ?? [];

        var competitors = benchmarks.OrganicResults.Take(request.CompetitorCount).Select(o => new BriefCompetitorSummary
        {
            Position = o.Position,
            Url = o.Url,
            Title = o.Title,
            WordCount = CountWords(o.Snippet) * 12,
        }).ToList();

        var terms = await BuildRecommendedTermsAsync(keyword, benchmarks, related, ct);
        var headings = BuildSuggestedHeadings(keyword, paa);

        return Result<ContentBrief>.Success(new ContentBrief
        {
            Keyword = keyword,
            Location = location,
            TargetWordCount = benchmarks.AvgWordCount,
            AvgTitleLength = benchmarks.AvgTitleLength,
            RecommendedTerms = terms,
            SuggestedHeadings = headings,
            TopCompetitors = competitors,
            PeopleAlsoAsk = paa.Select(p => p.Question).Where(q => q.Length > 0).ToList(),
            BenchmarkQuality = benchmarks.BenchmarkQuality,
        });
    }

    private async Task<Result<SeoSerpResult?>> EnsureSerpAsync(
        string keyword, string location, string languageCode, CancellationToken ct)
    {
        var cache = await serpCache.GetAsync(keyword, location, languageCode, ct);
        if (!cache.IsSuccess)
            return Result<SeoSerpResult?>.Failure(cache.Error ?? "SERP cache error");

        if (cache.Value is not null && cache.Value.ExpiresAt > DateTimeOffset.UtcNow)
            return Result<SeoSerpResult?>.Success(cache.Value);

        var fetch = await serpProvider.GetSerpResultsAsync(new SerpRequest
        {
            Keyword = keyword,
            Location = location,
            LanguageCode = languageCode,
            ResultCount = 10,
        }, ct);

        if (!fetch.IsSuccess || fetch.Value is null)
            return Result<SeoSerpResult?>.Failure(fetch.Error ?? "SERP fetch failed");

        var benchmarks = SerpBenchmarkCalculator.FromSerp(fetch.Value);
        var upserted = await serpCache.UpsertAsync(keyword, location, languageCode, fetch.Value, benchmarks, ct);
        return upserted.IsSuccess
            ? Result<SeoSerpResult?>.Success(upserted.Value)
            : Result<SeoSerpResult?>.Failure(upserted.Error ?? "SERP upsert failed");
    }

    private async Task<IReadOnlyList<string>> BuildRecommendedTermsAsync(
        string keyword,
        SerpBenchmarksPayload benchmarks,
        List<string> related,
        CancellationToken ct)
    {
        var snippets = benchmarks.OrganicResults
            .Take(5)
            .Select(o => $"{o.Title}: {o.Snippet}")
            .ToList();

        var aiResult = await ai.CompleteAsync(new AIRequest
        {
            SystemPrompt =
                "You extract SEO semantic terms. Respond with a JSON array of 8-12 short phrases only, no markdown.",
            UserPrompt =
                $"Keyword: {keyword}\n\nCompetitor snippets:\n{string.Join("\n", snippets)}\n\nRelated: {string.Join(", ", related.Take(8))}",
            MaxTokens = 512,
            Temperature = 0.3,
        }, ct);

        if (aiResult.IsSuccess && aiResult.Value is not null)
        {
            try
            {
                var terms = JsonSerializer.Deserialize<List<string>>(aiResult.Value.Content.Trim());
                if (terms is { Count: > 0 })
                    return terms;
            }
            catch
            {
                // fall through
            }
        }

        var fallback = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        fallback.AddRange(related.Take(5));
        return fallback.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
    }

    private static List<string> BuildSuggestedHeadings(string keyword, List<PeopleAlsoAskResult> paa)
    {
        var list = new List<string>
        {
            $"What is {keyword}?",
            $"Benefits of {keyword}",
            $"How to choose {keyword}",
            "Frequently asked questions",
        };
        list.AddRange(paa.Take(3).Select(p => p.Question));
        return list.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
    }

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
