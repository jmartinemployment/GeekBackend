using GeekSeo.Persistence.Entities;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class UrlResearchRepository(SeoDbContext db) : IUrlResearchRepository
{
    public async Task<Result<SeoUrlResearch>> CreateQueuedAsync(
        Guid userId, CreateUrlResearchQueuedRequest request, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var row = new SeoUrlResearch
        {
            ProjectId = request.ProjectId,
            UserId = userId,
            SourceUrl = request.SourceUrl.Trim(),
            Status = "queued",
            SupersedesResearchId = request.SupersedesResearchId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.UrlResearch.Add(row);
        await db.SaveChangesAsync(ct);
        return Result<SeoUrlResearch>.Success(row);
    }

    public async Task<Result<SeoUrlResearch>> GetFullAsync(Guid urlResearchId, CancellationToken ct = default)
    {
        var row = await db.UrlResearch.AsNoTracking()
            .Include(r => r.OrganicResults.OrderBy(o => o.Position))
            .Include(r => r.PeopleAlsoAsk.OrderBy(p => p.DisplayOrder))
            .Include(r => r.RelatedSearches.OrderBy(p => p.DisplayOrder))
            .Include(r => r.Competitors.OrderBy(c => c.Position))
                .ThenInclude(c => c.Headings.OrderBy(h => h.DisplayOrder))
            .Include(r => r.SourceHeadings.OrderBy(h => h.DisplayOrder))
            .Include(r => r.RecommendedTerms.OrderBy(t => t.DisplayOrder))
            .Include(r => r.ClosingFaqs.OrderBy(f => f.DisplayOrder))
            .Include(r => r.SectionHints.OrderBy(s => s.DisplayOrder))
            .FirstOrDefaultAsync(r => r.Id == urlResearchId, ct);

        return row is null
            ? Result<SeoUrlResearch>.NotFound("Page research not found")
            : Result<SeoUrlResearch>.Success(row);
    }

    public async Task<Result<IReadOnlyList<UrlResearchSummary>>> ListSummaryByProjectAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var list = await db.UrlResearch.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new UrlResearchSummary
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                SourceUrl = r.SourceUrl,
                DerivedKeyword = r.DerivedKeyword,
                Status = r.Status,
                DataQuality = r.DataQuality,
                ResearchedAt = r.ResearchedAt,
                CreatedAt = r.CreatedAt,
            })
            .ToListAsync(ct);

        return Result<IReadOnlyList<UrlResearchSummary>>.Success(list);
    }

    public async Task<Result<SeoUrlResearch>> PersistFullAsync(
        Guid urlResearchId, UrlResearchFullWrite body, CancellationToken ct = default)
    {
        var row = await db.UrlResearch
            .Include(r => r.OrganicResults)
            .Include(r => r.PeopleAlsoAsk)
            .Include(r => r.RelatedSearches)
            .Include(r => r.Competitors).ThenInclude(c => c.Headings)
            .Include(r => r.SourceHeadings)
            .Include(r => r.RecommendedTerms)
            .Include(r => r.ClosingFaqs)
            .Include(r => r.SectionHints)
            .FirstOrDefaultAsync(r => r.Id == urlResearchId, ct);

        if (row is null)
            return Result<SeoUrlResearch>.NotFound("Page research not found");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            ApplyParentFields(row, body);
            ClearChildren(row);
            AddChildren(row, body);
            row.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await GetFullAsync(urlResearchId, ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return Result<SeoUrlResearch>.Failure($"Failed to persist page research: {ex.Message}");
        }
    }

    public async Task<Result<SeoUrlResearch>> UpdateStatusAsync(
        Guid urlResearchId, UrlResearchStatusPatch patch, CancellationToken ct = default)
    {
        var row = await db.UrlResearch.FirstOrDefaultAsync(r => r.Id == urlResearchId, ct);
        if (row is null)
            return Result<SeoUrlResearch>.NotFound("Page research not found");

        row.Status = patch.Status;
        row.ErrorMessage = patch.ErrorMessage;
        if (patch.ResearchedAt.HasValue)
            row.ResearchedAt = patch.ResearchedAt;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result<SeoUrlResearch>.Success(row);
    }

    public async Task<Result<IReadOnlyList<UrlResearchQueuedJob>>> ListQueuedAsync(
        int limit, CancellationToken ct = default)
    {
        var list = await (
            from research in db.UrlResearch.AsNoTracking()
            where research.Status == "queued"
            orderby research.CreatedAt
            select new UrlResearchQueuedJob(
                research.Id,
                research.ProjectId,
                research.UserId,
                research.SourceUrl))
            .Take(Math.Clamp(limit, 1, 20))
            .ToListAsync(ct);

        return Result<IReadOnlyList<UrlResearchQueuedJob>>.Success(list);
    }

    public async Task<Result<int>> FailStaleRunningAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var stale = await db.UrlResearch
            .Where(r => r.Status == "running" && r.UpdatedAt < cutoff)
            .ToListAsync(ct);

        foreach (var row in stale)
        {
            row.Status = "failed";
            row.ErrorMessage = "Job timed out while running.";
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (stale.Count > 0)
            await db.SaveChangesAsync(ct);

        return Result<int>.Success(stale.Count);
    }

    public async Task<Result<bool>> TryClaimRunningAsync(Guid urlResearchId, CancellationToken ct = default)
    {
        var row = await db.UrlResearch.FirstOrDefaultAsync(r => r.Id == urlResearchId, ct);
        if (row is null)
            return Result<bool>.Success(false);
        if (!string.Equals(row.Status, "queued", StringComparison.Ordinal))
            return Result<bool>.Success(false);

        row.Status = "running";
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private static void ApplyParentFields(SeoUrlResearch row, UrlResearchFullWrite body)
    {
        row.DerivedKeyword = body.DerivedKeyword;
        row.SearchLocation = body.SearchLocation;
        row.BusinessContext = body.BusinessContext;
        row.GbpSource = body.GbpSource;
        row.Status = body.Status;
        row.ErrorMessage = body.ErrorMessage;
        row.DataQuality = body.DataQuality;
        row.DataQualityNotes = body.DataQualityNotes;
        row.IntentPrimary = body.IntentPrimary;
        row.IntentJustification = body.IntentJustification;
        row.PafType = body.PafType;
        row.PafFormat = body.PafFormat;
        row.PafText = body.PafText;
        row.PafSourceUrl = body.PafSourceUrl;
        row.PafBeatStrategy = body.PafBeatStrategy;
        row.DirectAnswerInstruction = body.DirectAnswerInstruction;
        row.MustBeatPaf = body.MustBeatPaf;
        row.MedianWordCountTop5 = body.MedianWordCountTop5;
        row.MedianTitleLengthTop10 = body.MedianTitleLengthTop10;
        row.MedianH2CountTop5 = body.MedianH2CountTop5;
        row.DominantContentFormat = body.DominantContentFormat;
        row.ResearchedAt = body.ResearchedAt;
    }

    private static void ClearChildren(SeoUrlResearch row)
    {
        foreach (var competitor in row.Competitors)
            competitor.Headings.Clear();
        row.OrganicResults.Clear();
        row.PeopleAlsoAsk.Clear();
        row.RelatedSearches.Clear();
        row.Competitors.Clear();
        row.SourceHeadings.Clear();
        row.RecommendedTerms.Clear();
        row.ClosingFaqs.Clear();
        row.SectionHints.Clear();
    }

    private static void AddChildren(SeoUrlResearch row, UrlResearchFullWrite body)
    {
        foreach (var organic in body.Organic)
        {
            row.OrganicResults.Add(new SeoUrlResearchOrganic
            {
                UrlResearchId = row.Id,
                Position = organic.Position,
                Url = organic.Url,
                Domain = organic.Domain,
                Title = organic.Title,
                Snippet = organic.Snippet,
                ContentType = organic.ContentType,
            });
        }

        foreach (var paa in body.PeopleAlsoAsk)
        {
            row.PeopleAlsoAsk.Add(new SeoUrlResearchPaa
            {
                UrlResearchId = row.Id,
                Question = paa.Question,
                SerpAnswerPreview = paa.SerpAnswerPreview,
                Depth = paa.Depth,
                DisplayOrder = paa.DisplayOrder,
            });
        }

        foreach (var pasf in body.RelatedSearches)
        {
            row.RelatedSearches.Add(new SeoUrlResearchPasf
            {
                UrlResearchId = row.Id,
                SearchText = pasf.SearchText,
                DisplayOrder = pasf.DisplayOrder,
            });
        }

        foreach (var competitor in body.Competitors)
        {
            var entity = new SeoUrlResearchCompetitor
            {
                UrlResearchId = row.Id,
                Url = competitor.Url,
                Position = competitor.Position,
                H1 = competitor.H1,
                EstimatedWordCount = competitor.EstimatedWordCount,
            };
            var order = 0;
            foreach (var heading in competitor.Headings)
            {
                entity.Headings.Add(new SeoUrlResearchCompetitorHeading
                {
                    Level = heading.Level,
                    Text = heading.Text,
                    DisplayOrder = heading.DisplayOrder > 0 ? heading.DisplayOrder : order++,
                });
            }

            row.Competitors.Add(entity);
        }

        foreach (var heading in body.SourceHeadings)
        {
            row.SourceHeadings.Add(new SeoUrlResearchSourceHeading
            {
                UrlResearchId = row.Id,
                Level = heading.Level,
                Text = heading.Text,
                DisplayOrder = heading.DisplayOrder,
            });
        }

        foreach (var term in body.RecommendedTerms)
        {
            row.RecommendedTerms.Add(new SeoUrlResearchTerm
            {
                UrlResearchId = row.Id,
                Term = term.Term,
                DisplayOrder = term.DisplayOrder,
            });
        }

        foreach (var faq in body.ClosingFaqs)
        {
            row.ClosingFaqs.Add(new SeoUrlResearchClosingFaq
            {
                UrlResearchId = row.Id,
                Question = faq.Question,
                Source = faq.Source,
                DisplayOrder = faq.DisplayOrder,
            });
        }

        foreach (var hint in body.SectionHints)
        {
            row.SectionHints.Add(new SeoUrlResearchSectionHint
            {
                UrlResearchId = row.Id,
                DisplayOrder = hint.DisplayOrder,
                Movement = hint.Movement,
                Label = hint.Label,
                SuggestedH2 = hint.SuggestedH2,
                SubtopicsFromSerp = hint.SubtopicsFromSerp.ToArray(),
            });
        }
    }
}
