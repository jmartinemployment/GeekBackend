using System.Text.Json;
using GeekSeo.Persistence.Entities;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class CompetitorPageRepository(SeoDbContext db) : ICompetitorPageRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<Result<IReadOnlyList<SeoCompetitorPage>>> GetBySerpResultAsync(
        Guid serpResultId, CancellationToken ct = default)
    {
        var rows = await db.CompetitorPages.AsNoTracking()
            .Where(p => p.SerpResultId == serpResultId)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoCompetitorPage>>.Success(rows);
    }

    public async Task<Result<SeoCompetitorPage>> UpsertAsync(
        Guid serpResultId, PageContent page, CancellationToken ct = default)
    {
        var existing = await db.CompetitorPages.FirstOrDefaultAsync(
            p => p.SerpResultId == serpResultId && p.Url == page.Url, ct);

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(72);
        var domain = Uri.TryCreate(page.Url, UriKind.Absolute, out var uri) ? uri.Host : null;

        if (existing is null)
        {
            existing = new SeoCompetitorPage
            {
                Id = Guid.NewGuid(),
                SerpResultId = serpResultId,
                Url = page.Url,
            };
            db.CompetitorPages.Add(existing);
        }

        existing.Domain = domain;
        existing.MetaTitle = page.MetaTitle;
        existing.MetaDescription = page.MetaDescription;
        existing.ContentText = page.FullText;
        existing.WordCount = page.WordCount;
        existing.HeadingsJson = JsonSerializer.Serialize(page.Headings, JsonOptions);
        existing.HasStructuredData = page.HasStructuredData;
        existing.StructuredDataTypesJson = JsonSerializer.Serialize(page.StructuredDataTypes, JsonOptions);
        existing.HttpStatus = page.HttpStatusCode;
        existing.CrawledAt = page.CrawledAt;
        existing.ExpiresAt = expires;

        await db.SaveChangesAsync(ct);
        return Result<SeoCompetitorPage>.Success(existing);
    }
}
