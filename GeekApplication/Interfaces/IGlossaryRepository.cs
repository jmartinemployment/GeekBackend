using GeekApplication.Models.Glossary;

namespace GeekApplication.Interfaces;

public interface IGlossaryRepository
{
    Task<IReadOnlyList<GlossaryTermSummaryDto>> GetAllPublishedAsync(CancellationToken ct = default);

    Task<GlossaryTermDto?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<GlossaryTermDto> CreateAsync(GlossaryTermWriteRequest request, CancellationToken ct = default);

    Task<GlossaryTermDto?> UpdateAsync(string slug, GlossaryTermWriteRequest request, CancellationToken ct = default);

    Task<bool> DeleteAsync(string slug, CancellationToken ct = default);
}
