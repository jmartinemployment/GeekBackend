using GeekApplication.Models.Glossary;

namespace GeekApplication.Interfaces;

/// <summary>
/// Glossary reads. Read-only since 2026-09-29: Create, Update and Delete were removed with the
/// last thing that could reach them.
///
/// <para>
/// geekatyourspot stopped reading the glossary from this API on 2026-09-28 and now serves it from
/// <c>src/data/glossary/terms.ts</c>, saying why: "Editing a term is a code change and a deploy,
/// not a database update." GeekAPI's own GlossaryController lost its admin CRUD the same day, which
/// left this interface's three write methods, HttpGlossaryRepository's implementations of them,
/// GeekRepository's POST/PUT/DELETE on <c>repo/content/glossary</c>, and GlossaryRepository's SQL
/// forming a closed loop with no entry point.
/// </para>
/// </summary>
public interface IGlossaryRepository
{
    Task<IReadOnlyList<GlossaryTermSummaryDto>> GetAllPublishedAsync(CancellationToken ct = default);

    Task<GlossaryTermDto?> GetBySlugAsync(string slug, CancellationToken ct = default);
}
