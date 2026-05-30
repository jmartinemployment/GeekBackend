using GeekSeo.Persistence.Entities;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class BrandVoiceRepository(SeoDbContext db) : IBrandVoiceRepository
{
    public async Task<Result<IReadOnlyList<SeoBrandVoice>>> ListByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await db.BrandVoices.AsNoTracking()
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoBrandVoice>>.Success(list);
    }

    public async Task<Result<SeoBrandVoice>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.BrandVoices.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
        return row is null
            ? Result<SeoBrandVoice>.NotFound("Brand voice not found")
            : Result<SeoBrandVoice>.Success(row);
    }

    public async Task<Result<SeoBrandVoice>> CreateAsync(
        Guid userId, CreateBrandVoiceRequest request, CancellationToken ct = default)
    {
        var row = new SeoBrandVoice
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name.Trim(),
            SampleText = request.SampleText.Trim(),
            StyleInstructions = request.StyleInstructions?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.BrandVoices.Add(row);
        await db.SaveChangesAsync(ct);
        return Result<SeoBrandVoice>.Success(row);
    }

    public async Task<Result<SeoBrandVoice>> UpdateAsync(
        Guid userId, Guid id, UpdateBrandVoiceRequest request, CancellationToken ct = default)
    {
        var row = await db.BrandVoices.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId, ct);
        if (row is null)
            return Result<SeoBrandVoice>.NotFound("Brand voice not found");

        if (request.Name is not null) row.Name = request.Name.Trim();
        if (request.SampleText is not null) row.SampleText = request.SampleText.Trim();
        if (request.StyleInstructions is not null) row.StyleInstructions = request.StyleInstructions.Trim();

        await db.SaveChangesAsync(ct);
        return Result<SeoBrandVoice>.Success(row);
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var row = await db.BrandVoices.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId, ct);
        if (row is null)
            return Result.Failure("Brand voice not found");
        db.BrandVoices.Remove(row);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
