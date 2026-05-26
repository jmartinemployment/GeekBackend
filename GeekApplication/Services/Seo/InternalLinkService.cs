using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class InternalLinkService(
    IProjectRepository projects,
    IContentDocumentRepository documents) : IInternalLinkService
{
    public async Task<Result<IReadOnlyList<InternalLinkSuggestion>>> SuggestAsync(
        Guid userId, InternalLinkSuggestRequest request, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(request.ProjectId, ct);
        if (!project.IsSuccess || project.Value is null || project.Value.UserId != userId)
            return Result<IReadOnlyList<InternalLinkSuggestion>>.Failure("Access denied");

        var doc = await documents.GetByIdAsync(request.DocumentId, ct);
        if (!doc.IsSuccess || doc.Value is null || doc.Value.ProjectId != request.ProjectId)
            return Result<IReadOnlyList<InternalLinkSuggestion>>.Failure("Document not found");

        var allDocs = await documents.GetByProjectAsync(request.ProjectId, ct);
        var siblings = (allDocs.Value ?? [])
            .Where(d => d.Id != request.DocumentId && !string.IsNullOrWhiteSpace(d.TargetKeyword))
            .ToList();

        var keyword = doc.Value.TargetKeyword.Trim();
        var suggestions = new List<InternalLinkSuggestion>();
        var max = Math.Clamp(request.MaxSuggestions, 1, 25);

        foreach (var sibling in siblings)
        {
            if (suggestions.Count >= max) break;
            var overlap = KeywordOverlap(keyword, sibling.TargetKeyword);
            if (overlap < 0.15) continue;

            suggestions.Add(new InternalLinkSuggestion
            {
                AnchorText = sibling.Title.Length > 0 ? sibling.Title : sibling.TargetKeyword,
                TargetUrl = $"/app/content/{sibling.Id}",
                Reason = $"Related article in this project ({sibling.TargetKeyword})",
                RelevanceScore = overlap,
            });
        }

        return Result<IReadOnlyList<InternalLinkSuggestion>>.Success(
            suggestions.OrderByDescending(s => s.RelevanceScore).Take(max).ToList());
    }

    private static double KeywordOverlap(string a, string b)
    {
        var setA = a.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var setB = b.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (setA.Count == 0 || setB.Count == 0) return 0;
        var intersection = setA.Intersect(setB).Count();
        return intersection / (double)Math.Max(setA.Count, setB.Count);
    }

}
