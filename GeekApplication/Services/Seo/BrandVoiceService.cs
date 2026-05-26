using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class BrandVoiceService(IBrandVoiceRepository repo) : IBrandVoiceService
{
    public async Task<Result<IReadOnlyList<BrandVoiceDto>>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await repo.ListByUserAsync(userId, ct);
        if (!rows.IsSuccess || rows.Value is null)
            return Result<IReadOnlyList<BrandVoiceDto>>.Failure(rows.Error ?? "Failed to list brand voices");
        return Result<IReadOnlyList<BrandVoiceDto>>.Success(rows.Value.Select(ToDto).ToList());
    }

    public async Task<Result<BrandVoiceDto>> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var row = await repo.GetByIdAsync(id, ct);
        if (!row.IsSuccess || row.Value is null)
            return Result<BrandVoiceDto>.Failure(row.Error ?? "Not found");
        if (row.Value.UserId != userId)
            return Result<BrandVoiceDto>.Failure("Access denied");
        return Result<BrandVoiceDto>.Success(ToDto(row.Value));
    }

    public async Task<Result<BrandVoiceDto>> CreateAsync(
        Guid userId, CreateBrandVoiceRequest request, CancellationToken ct = default)
    {
        var created = await repo.CreateAsync(userId, request, ct);
        if (!created.IsSuccess || created.Value is null)
            return Result<BrandVoiceDto>.Failure(created.Error ?? "Create failed");
        return Result<BrandVoiceDto>.Success(ToDto(created.Value));
    }

    public async Task<Result<BrandVoiceDto>> UpdateAsync(
        Guid userId, Guid id, UpdateBrandVoiceRequest request, CancellationToken ct = default)
    {
        var updated = await repo.UpdateAsync(userId, id, request, ct);
        if (!updated.IsSuccess || updated.Value is null)
            return Result<BrandVoiceDto>.Failure(updated.Error ?? "Update failed");
        return Result<BrandVoiceDto>.Success(ToDto(updated.Value));
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
        => await repo.DeleteAsync(userId, id, ct);

    private static BrandVoiceDto ToDto(Entities.Seo.SeoBrandVoice v) => new()
    {
        Id = v.Id,
        Name = v.Name,
        SampleText = v.SampleText,
        StyleInstructions = v.StyleInstructions,
        CreatedAt = v.CreatedAt,
    };
}
