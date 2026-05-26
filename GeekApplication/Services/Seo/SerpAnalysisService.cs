using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class SerpAnalysisService(ISerpProvider serp) : ISerpAnalysisService
{
    public async Task<Result<DeepSerpResult>> AnalyzeAsync(
        Guid userId, DeepSerpRequest request, CancellationToken ct = default)
    {
        _ = userId;
        var serpResult = await serp.GetSerpResultsAsync(new SerpRequest
        {
            Keyword = request.Keyword.Trim(),
            Location = request.Location,
            LanguageCode = request.LanguageCode,
            ResultCount = 20,
        }, ct);

        if (!serpResult.IsSuccess || serpResult.Value is null)
            return Result<DeepSerpResult>.Failure(serpResult.Error ?? "SERP fetch failed");

        var value = serpResult.Value;
        var organic = value.OrganicResults.Select(o => new DeepSerpOrganic
        {
            Position = o.Position,
            Url = o.Url,
            Title = o.Title,
            Snippet = o.Snippet,
            Domain = o.Domain,
        }).ToList();

        var snippetLengths = organic
            .Select(o => o.Snippet?.Length ?? 0)
            .Where(len => len > 0)
            .ToList();

        return Result<DeepSerpResult>.Success(new DeepSerpResult
        {
            Keyword = value.Keyword,
            Location = value.Location,
            Provider = serp.ProviderName,
            Organic = organic,
            PeopleAlsoAsk = value.PeopleAlsoAsk.Select(p => p.Question).ToList(),
            RelatedSearches = value.RelatedSearches.ToList(),
            Intent = InferIntent(request.Keyword, value, snippetLengths),
        });
    }

    private static SerpIntentSummary InferIntent(string keyword, SerpResult serp, IReadOnlyList<int> snippetLengths)
    {
        var lower = keyword.ToLowerInvariant();
        var formats = new List<string>();

        if (serp.Features.HasFeaturedSnippet) formats.Add("featured_snippet");
        if (serp.Features.HasPeopleAlsoAsk) formats.Add("faq");
        if (serp.Features.HasVideoCarousel) formats.Add("video");
        if (serp.Features.HasLocalPack) formats.Add("local");
        if (formats.Count == 0) formats.Add("article");

        var primary = lower.Contains("how to", StringComparison.Ordinal) || lower.StartsWith("how ", StringComparison.Ordinal)
            ? "informational"
            : lower.Contains("buy", StringComparison.Ordinal) || lower.Contains("price", StringComparison.Ordinal)
                ? "commercial"
                : lower.Contains("near me", StringComparison.Ordinal)
                    ? "local"
                    : "informational";

        return new SerpIntentSummary
        {
            PrimaryIntent = primary,
            ContentFormats = formats,
            AvgSnippetLength = snippetLengths.Count > 0 ? snippetLengths.Average() : 0,
        };
    }
}
