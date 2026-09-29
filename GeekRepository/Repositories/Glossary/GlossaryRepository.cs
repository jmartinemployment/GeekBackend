using Dapper;
using GeekApplication.Interfaces;
using GeekApplication.Models.Glossary;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories.Glossary;

public sealed class GlossaryRepository : IGlossaryRepository
{
    private readonly IAmbientDbContext _ambient;

    public GlossaryRepository(IAmbientDbContext ambient) => _ambient = ambient;

    public async Task<IReadOnlyList<GlossaryTermSummaryDto>> GetAllPublishedAsync(
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                slug::text       AS Slug,
                title            AS Title,
                category         AS Category,
                short_summary    AS ShortSummary
            FROM geek_glossary.terms
            WHERE status = 'published'
            ORDER BY title
            """;

        var command = new CommandDefinition(
            sql,
            transaction: _ambient.Transaction,
            cancellationToken: ct);

        var rows = await _ambient.Connection.QueryAsync<GlossaryTermSummaryDto>(command);
        return rows.ToList();
    }

    public async Task<GlossaryTermDto?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        const string termSql = """
            SELECT
                id               AS Id,
                slug::text       AS Slug,
                title            AS Title,
                category         AS Category,
                short_summary    AS ShortSummary,
                status           AS Status,
                created_at       AS CreatedAt,
                updated_at       AS UpdatedAt
            FROM geek_glossary.terms
            WHERE slug = @Slug
            """;

        var termCommand = new CommandDefinition(
            termSql,
            new { Slug = slug },
            _ambient.Transaction,
            cancellationToken: ct);

        var term = await _ambient.Connection.QuerySingleOrDefaultAsync<GlossaryTermDto>(termCommand);
        if (term is null) return null;

        const string defsSql = """
            SELECT
                sort_order       AS SortOrder,
                part_of_speech   AS PartOfSpeech,
                text             AS Text,
                example          AS Example
            FROM geek_glossary.term_definitions
            WHERE term_id = @TermId
            ORDER BY sort_order
            """;

        var defsCommand = new CommandDefinition(
            defsSql,
            new { TermId = term.Id },
            _ambient.Transaction,
            cancellationToken: ct);

        var definitions = (await _ambient.Connection.QueryAsync<GlossaryDefinitionDto>(defsCommand)).ToList();
        return term with { Definitions = definitions };
    }
}
