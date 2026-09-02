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

    public async Task<GlossaryTermDto> CreateAsync(
        GlossaryTermWriteRequest request,
        CancellationToken ct = default)
    {
        const string insertTermSql = """
            INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
            VALUES (@Slug, @Title, @Category, @ShortSummary, @Status)
            RETURNING id
            """;

        var termId = await _ambient.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                insertTermSql,
                new
                {
                    request.Slug,
                    request.Title,
                    request.Category,
                    request.ShortSummary,
                    request.Status,
                },
                _ambient.Transaction,
                cancellationToken: ct));

        await InsertDefinitionsAsync(termId, request.Definitions, ct);

        return (await GetBySlugAsync(request.Slug, ct))!;
    }

    public async Task<GlossaryTermDto?> UpdateAsync(
        string slug,
        GlossaryTermWriteRequest request,
        CancellationToken ct = default)
    {
        const string updateTermSql = """
            UPDATE geek_glossary.terms
            SET slug = @NewSlug,
                title = @Title,
                category = @Category,
                short_summary = @ShortSummary,
                status = @Status,
                updated_at = NOW()
            WHERE slug = @Slug
            RETURNING id
            """;

        var termId = await _ambient.Connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                updateTermSql,
                new
                {
                    Slug = slug,
                    NewSlug = request.Slug,
                    request.Title,
                    request.Category,
                    request.ShortSummary,
                    request.Status,
                },
                _ambient.Transaction,
                cancellationToken: ct));

        if (termId is null) return null;

        const string deleteDefsSql = """
            DELETE FROM geek_glossary.term_definitions WHERE term_id = @TermId
            """;

        await _ambient.Connection.ExecuteAsync(
            new CommandDefinition(
                deleteDefsSql,
                new { TermId = termId.Value },
                _ambient.Transaction,
                cancellationToken: ct));

        await InsertDefinitionsAsync(termId.Value, request.Definitions, ct);

        return await GetBySlugAsync(request.Slug, ct);
    }

    public async Task<bool> DeleteAsync(string slug, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM geek_glossary.terms WHERE slug = @Slug
            """;

        var affected = await _ambient.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { Slug = slug }, _ambient.Transaction, cancellationToken: ct));

        return affected > 0;
    }

    private async Task InsertDefinitionsAsync(
        int termId,
        IReadOnlyList<GlossaryDefinitionWriteRequest> definitions,
        CancellationToken ct)
    {
        const string insertDefSql = """
            INSERT INTO geek_glossary.term_definitions
                (term_id, sort_order, part_of_speech, text, example)
            VALUES (@TermId, @SortOrder, @PartOfSpeech, @Text, @Example)
            """;

        foreach (var def in definitions)
        {
            await _ambient.Connection.ExecuteAsync(
                new CommandDefinition(
                    insertDefSql,
                    new
                    {
                        TermId = termId,
                        def.SortOrder,
                        def.PartOfSpeech,
                        def.Text,
                        def.Example,
                    },
                    _ambient.Transaction,
                    cancellationToken: ct));
        }
    }
}
