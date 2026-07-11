using Dapper;
using GeekApplication.Interfaces;
using GeekApplication.Models.Blog;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories.Blog;

/// <summary>
/// Dapper repository for geek_blog schema. All queries target the isolated geek_blog namespace.
/// </summary>
public sealed class BlogRepository : IBlogRepository
{
    private readonly IAmbientDbContext _ambient;

    public BlogRepository(IAmbientDbContext ambient) => _ambient = ambient;

    public async Task<bool> UserHasPermissionAsync(
        int userId,
        string permissionName,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM geek_blog.user_roles ur
                INNER JOIN geek_blog.role_permissions rp ON rp.role_id = ur.role_id
                INNER JOIN geek_blog.permissions p ON p.id = rp.permission_id
                WHERE ur.user_id = @UserId
                  AND p.name = @PermissionName
            )
            """;

        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("PermissionName", permissionName);

        var command = new CommandDefinition(
            sql,
            parameters,
            _ambient.Transaction,
            cancellationToken: ct);

        return await _ambient.Connection.ExecuteScalarAsync<bool>(command);
    }

    public async Task<IReadOnlyList<BlogPostFlatDto>> SearchPostsWithOptimizedPlanAsync(
        string searchTerm,
        string languageCode,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                p.id              AS PostId,
                p.post_type       AS PostType,
                pt.language_code  AS LanguageCode,
                pt.slug::text     AS Slug,
                pt.title          AS Title,
                pt.body           AS Body,
                p.status          AS Status,
                p.published_at    AS PublishedAt,
                p.created_at      AS CreatedAt,
                p.updated_at      AS UpdatedAt,
                COALESCE(tags.localized_tags_json, '[]') AS LocalizedTagsJson,
                pt.schema_metadata::text AS SchemaMetadataJson,
                ts_rank(pt.search_vector, websearch_to_tsquery(geek_blog.resolve_ts_config(@LanguageCode), @SearchTerm)) AS SearchRank
            FROM geek_blog.post_translations pt
            INNER JOIN geek_blog.posts p ON p.id = pt.post_id
            LEFT JOIN LATERAL (
                SELECT json_agg(
                    json_build_object('slug', t.slug::text, 'name', tt.name)
                    ORDER BY tt.name
                )::text AS localized_tags_json
                FROM geek_blog.post_tags ptg
                INNER JOIN geek_blog.tags t ON t.id = ptg.tag_id
                INNER JOIN geek_blog.tag_translations tt
                    ON tt.tag_id = t.id AND tt.language_code = pt.language_code
                WHERE ptg.post_id = p.id
            ) tags ON TRUE
            WHERE pt.language_code = @LanguageCode
              AND p.status = 'published'
              AND pt.search_vector @@ websearch_to_tsquery(geek_blog.resolve_ts_config(@LanguageCode), @SearchTerm)
            ORDER BY SearchRank DESC, p.published_at DESC NULLS LAST
            """;

        var parameters = new DynamicParameters();
        parameters.Add("SearchTerm", searchTerm);
        parameters.Add("LanguageCode", languageCode);

        var command = new CommandDefinition(
            sql,
            parameters,
            _ambient.Transaction,
            cancellationToken: ct);

        var rows = await _ambient.Connection.QueryAsync<BlogPostFlatDto>(command);
        return rows.ToList();
    }

    public async Task<BlogPostFlatDto?> GetPostBySlugAsync(
        string slug,
        string languageCode,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                p.id              AS PostId,
                p.post_type       AS PostType,
                pt.language_code  AS LanguageCode,
                pt.slug::text     AS Slug,
                pt.title          AS Title,
                pt.body           AS Body,
                p.status          AS Status,
                p.published_at    AS PublishedAt,
                p.created_at      AS CreatedAt,
                p.updated_at      AS UpdatedAt,
                COALESCE(tags.localized_tags_json, '[]') AS LocalizedTagsJson,
                pt.schema_metadata::text AS SchemaMetadataJson
            FROM geek_blog.post_translations pt
            INNER JOIN geek_blog.posts p ON p.id = pt.post_id
            LEFT JOIN LATERAL (
                SELECT json_agg(
                    json_build_object('slug', t.slug::text, 'name', tt.name)
                    ORDER BY tt.name
                )::text AS localized_tags_json
                FROM geek_blog.post_tags ptg
                INNER JOIN geek_blog.tags t ON t.id = ptg.tag_id
                INNER JOIN geek_blog.tag_translations tt
                    ON tt.tag_id = t.id AND tt.language_code = pt.language_code
                WHERE ptg.post_id = p.id
            ) tags ON TRUE
            WHERE pt.slug = @Slug
              AND pt.language_code = @LanguageCode
            """;

        var parameters = new DynamicParameters();
        parameters.Add("Slug", slug);
        parameters.Add("LanguageCode", languageCode);

        var command = new CommandDefinition(
            sql,
            parameters,
            _ambient.Transaction,
            cancellationToken: ct);

        return await _ambient.Connection.QueryFirstOrDefaultAsync<BlogPostFlatDto>(command);
    }

    public async Task<IReadOnlyList<BlogPostFlatDto>> GetTechnicalArticlesOnlyAsync(
        string languageCode,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                p.id              AS PostId,
                p.post_type       AS PostType,
                pt.language_code  AS LanguageCode,
                pt.slug::text     AS Slug,
                pt.title          AS Title,
                pt.body           AS Body,
                p.status          AS Status,
                p.published_at    AS PublishedAt,
                p.created_at      AS CreatedAt,
                p.updated_at      AS UpdatedAt,
                COALESCE(tags.localized_tags_json, '[]') AS LocalizedTagsJson,
                pt.schema_metadata::text AS SchemaMetadataJson
            FROM geek_blog.posts p
            INNER JOIN geek_blog.post_translations pt
                ON pt.post_id = p.id AND pt.language_code = @LanguageCode
            LEFT JOIN LATERAL (
                SELECT json_agg(
                    json_build_object('slug', t.slug::text, 'name', tt.name)
                    ORDER BY tt.name
                )::text AS localized_tags_json
                FROM geek_blog.post_tags ptg
                INNER JOIN geek_blog.tags t ON t.id = ptg.tag_id
                INNER JOIN geek_blog.tag_translations tt
                    ON tt.tag_id = t.id AND tt.language_code = pt.language_code
                WHERE ptg.post_id = p.id
            ) tags ON TRUE
            WHERE p.post_type = 'TechnicalArticle'
              AND p.status = 'published'
            ORDER BY p.published_at DESC NULLS LAST, p.id DESC
            """;

        var parameters = new DynamicParameters();
        parameters.Add("LanguageCode", languageCode);

        var command = new CommandDefinition(
            sql,
            parameters,
            _ambient.Transaction,
            cancellationToken: ct);

        var rows = await _ambient.Connection.QueryAsync<BlogPostFlatDto>(command);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<CommentDto>> GetThreadedCommentsAsync(
        int postId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                c.id         AS Id,
                c.post_id    AS PostId,
                c.user_id    AS UserId,
                c.content    AS Content,
                c.path::text AS Path,
                nlevel(c.path) AS Depth,
                c.created_at AS CreatedAt
            FROM geek_blog.comments c
            WHERE c.post_id = @PostId
            ORDER BY c.path
            """;

        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);

        var command = new CommandDefinition(
            sql,
            parameters,
            _ambient.Transaction,
            cancellationToken: ct);

        var rows = await _ambient.Connection.QueryAsync<CommentDto>(command);
        return rows.ToList();
    }

    public async Task<int> InsertCommentReplyWithoutLocalTransactionAsync(
        int postId,
        int? userId,
        string content,
        string? parentPath,
        CancellationToken ct = default)
    {
        const string insertSql = """
            WITH new_id AS (
                SELECT nextval(pg_get_serial_sequence('geek_blog.comments', 'id')) AS id
            )
            INSERT INTO geek_blog.comments (id, post_id, user_id, content, path)
            SELECT
                new_id.id,
                @PostId,
                @UserId,
                @Content,
                CASE
                    WHEN @ParentPath IS NULL THEN text2ltree(new_id.id::text)
                    ELSE text2ltree(@ParentPath) || text2ltree(new_id.id::text)
                END
            FROM new_id
            RETURNING id
            """;

        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        parameters.Add("UserId", userId);
        parameters.Add("Content", content);
        parameters.Add("ParentPath", parentPath);

        var insertCommand = new CommandDefinition(
            insertSql,
            parameters,
            _ambient.Transaction,
            cancellationToken: ct);

        return await _ambient.Connection.ExecuteScalarAsync<int>(insertCommand);
    }
}
