using Dapper;
using GeekApplication.Blog;
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
                pt.department_id AS DepartmentId,
                d.slug AS DepartmentSlug,
                COALESCE(pres.presentation_json, '{}') AS PresentationJson,
                pt.hero_image_url AS HeroImageUrl,
                pt.source_project_id AS SourceProjectId,
                pt.content_role AS ContentRole,
                pt.source_pillar_slug AS SourcePillarSlug,
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
            LEFT JOIN public.departments d ON d.id = pt.department_id
            LEFT JOIN LATERAL (
                SELECT COALESCE(
                    json_object_agg(pp.surface, pp.copy) FILTER (WHERE pp.surface IS NOT NULL),
                    '{}'
                )::text AS presentation_json
                FROM geek_blog.post_presentation pp
                WHERE pp.post_translation_id = pt.id
            ) pres ON TRUE
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
                pt.schema_metadata::text AS SchemaMetadataJson,
                pt.department_id AS DepartmentId,
                d.slug AS DepartmentSlug,
                COALESCE(pres.presentation_json, '{}') AS PresentationJson,
                pt.hero_image_url AS HeroImageUrl,
                pt.source_project_id AS SourceProjectId,
                pt.content_role AS ContentRole,
                pt.source_pillar_slug AS SourcePillarSlug
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
            LEFT JOIN public.departments d ON d.id = pt.department_id
            LEFT JOIN LATERAL (
                SELECT COALESCE(
                    json_object_agg(pp.surface, pp.copy) FILTER (WHERE pp.surface IS NOT NULL),
                    '{}'
                )::text AS presentation_json
                FROM geek_blog.post_presentation pp
                WHERE pp.post_translation_id = pt.id
            ) pres ON TRUE
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
                pt.schema_metadata::text AS SchemaMetadataJson,
                pt.department_id AS DepartmentId,
                d.slug AS DepartmentSlug,
                COALESCE(pres.presentation_json, '{}') AS PresentationJson,
                pt.hero_image_url AS HeroImageUrl,
                pt.source_project_id AS SourceProjectId,
                pt.content_role AS ContentRole,
                pt.source_pillar_slug AS SourcePillarSlug
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
            LEFT JOIN public.departments d ON d.id = pt.department_id
            LEFT JOIN LATERAL (
                SELECT COALESCE(
                    json_object_agg(pp.surface, pp.copy) FILTER (WHERE pp.surface IS NOT NULL),
                    '{}'
                )::text AS presentation_json
                FROM geek_blog.post_presentation pp
                WHERE pp.post_translation_id = pt.id
            ) pres ON TRUE
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

    public async Task<IReadOnlyList<BlogPostFlatDto>> GetAllPostsAsync(
        string? languageCode = null,
        string? status = null,
        string? postType = null,
        CancellationToken ct = default)
    {
        var sql = """
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
                pt.department_id AS DepartmentId,
                d.slug AS DepartmentSlug,
                COALESCE(pres.presentation_json, '{}') AS PresentationJson,
                pt.hero_image_url AS HeroImageUrl,
                pt.source_project_id AS SourceProjectId,
                pt.content_role AS ContentRole,
                pt.source_pillar_slug AS SourcePillarSlug
            FROM geek_blog.posts p
            INNER JOIN geek_blog.post_translations pt ON pt.post_id = p.id
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
            LEFT JOIN public.departments d ON d.id = pt.department_id
            LEFT JOIN LATERAL (
                SELECT COALESCE(
                    json_object_agg(pp.surface, pp.copy) FILTER (WHERE pp.surface IS NOT NULL),
                    '{}'
                )::text AS presentation_json
                FROM geek_blog.post_presentation pp
                WHERE pp.post_translation_id = pt.id
            ) pres ON TRUE
            WHERE 1=1
            """;

        if (languageCode is not null)
            sql += " AND pt.language_code = @LanguageCode";
        if (status is not null)
            sql += " AND p.status = @Status";
        if (postType is not null)
            sql += " AND p.post_type = @PostType";

        sql += " ORDER BY p.updated_at DESC, p.id DESC";

        var parameters = new DynamicParameters();
        if (languageCode is not null) parameters.Add("LanguageCode", languageCode);
        if (status is not null) parameters.Add("Status", status);
        if (postType is not null) parameters.Add("PostType", postType);

        var command = new CommandDefinition(sql, parameters, _ambient.Transaction, cancellationToken: ct);
        var rows = await _ambient.Connection.QueryAsync<BlogPostFlatDto>(command);
        return rows.ToList();
    }

    public async Task<BlogPostFlatDto?> GetPostByIdAsync(
        int postId,
        string? languageCode = null,
        CancellationToken ct = default)
    {
        var sql = """
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
                pt.department_id AS DepartmentId,
                d.slug AS DepartmentSlug,
                COALESCE(pres.presentation_json, '{}') AS PresentationJson,
                pt.hero_image_url AS HeroImageUrl,
                pt.source_project_id AS SourceProjectId,
                pt.content_role AS ContentRole,
                pt.source_pillar_slug AS SourcePillarSlug
            FROM geek_blog.posts p
            INNER JOIN geek_blog.post_translations pt ON pt.post_id = p.id
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
            LEFT JOIN public.departments d ON d.id = pt.department_id
            LEFT JOIN LATERAL (
                SELECT COALESCE(
                    json_object_agg(pp.surface, pp.copy) FILTER (WHERE pp.surface IS NOT NULL),
                    '{}'
                )::text AS presentation_json
                FROM geek_blog.post_presentation pp
                WHERE pp.post_translation_id = pt.id
            ) pres ON TRUE
            WHERE p.id = @PostId
            """ + (languageCode is not null ? " AND pt.language_code = @LanguageCode" : "") + """
             ORDER BY pt.language_code
             LIMIT 1
            """;

        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        if (languageCode is not null) parameters.Add("LanguageCode", languageCode);

        var command = new CommandDefinition(sql, parameters, _ambient.Transaction, cancellationToken: ct);
        return await _ambient.Connection.QueryFirstOrDefaultAsync<BlogPostFlatDto>(command);
    }

    public async Task<int> CreatePostAsync(UpsertBlogPostCommand command, CancellationToken ct = default)
    {
        var publishedAt = ResolvePublishedAt(command);

        const string insertPostSql = """
            INSERT INTO geek_blog.posts (post_type, author_id, status, published_at)
            VALUES (@PostType, @AuthorId, @Status, @PublishedAt)
            RETURNING id
            """;

        var postParams = new DynamicParameters();
        postParams.Add("PostType", command.PostType);
        postParams.Add("AuthorId", command.AuthorId);
        postParams.Add("Status", command.Status);
        postParams.Add("PublishedAt", publishedAt);

        var postId = await _ambient.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(insertPostSql, postParams, _ambient.Transaction, cancellationToken: ct));

        await UpsertTranslationAsync(postId, command, ct);
        await ReplacePostTagsAsync(postId, command.LanguageCode, command.TagSlugs, ct);

        return postId;
    }

    public async Task<bool> UpdatePostAsync(int postId, UpsertBlogPostCommand command, CancellationToken ct = default)
    {
        const string existsSql = "SELECT EXISTS(SELECT 1 FROM geek_blog.posts WHERE id = @PostId)";
        var exists = await _ambient.Connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(existsSql, new { PostId = postId }, _ambient.Transaction, cancellationToken: ct));
        if (!exists) return false;

        var publishedAt = ResolvePublishedAt(command);

        const string updatePostSql = """
            UPDATE geek_blog.posts
            SET post_type = @PostType,
                author_id = @AuthorId,
                status = @Status,
                published_at = @PublishedAt,
                updated_at = NOW()
            WHERE id = @PostId
            """;

        var postParams = new DynamicParameters();
        postParams.Add("PostId", postId);
        postParams.Add("PostType", command.PostType);
        postParams.Add("AuthorId", command.AuthorId);
        postParams.Add("Status", command.Status);
        postParams.Add("PublishedAt", publishedAt);

        await _ambient.Connection.ExecuteAsync(
            new CommandDefinition(updatePostSql, postParams, _ambient.Transaction, cancellationToken: ct));

        await UpsertTranslationAsync(postId, command, ct);
        await ReplacePostTagsAsync(postId, command.LanguageCode, command.TagSlugs, ct);

        return true;
    }

    public async Task<bool> DeletePostAsync(int postId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM geek_blog.posts WHERE id = @PostId";
        var affected = await _ambient.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { PostId = postId }, _ambient.Transaction, cancellationToken: ct));
        return affected > 0;
    }

    private static DateTimeOffset? ResolvePublishedAt(UpsertBlogPostCommand command) =>
        command.Status == "published"
            ? (command.PublishedAt ?? DateTimeOffset.UtcNow).ToUniversalTime()
            : null;

    private async Task UpsertTranslationAsync(int postId, UpsertBlogPostCommand command, CancellationToken ct)
    {
        var departmentId = await ResolveDepartmentIdAsync(command, ct);

        const string sql = """
            INSERT INTO geek_blog.post_translations
                (post_id, language_code, slug, title, body, post_type, schema_metadata,
                 department_id, hero_image_url,
                 source_project_id, content_role, source_pillar_slug)
            VALUES
                (@PostId, @LanguageCode, @Slug, @Title, @Body, @PostType, @SchemaMetadata::jsonb,
                 @DepartmentId, @HeroImageUrl,
                 @SourceProjectId, @ContentRole, @SourcePillarSlug)
            ON CONFLICT (post_id, language_code) DO UPDATE SET
                slug = EXCLUDED.slug,
                title = EXCLUDED.title,
                body = EXCLUDED.body,
                post_type = EXCLUDED.post_type,
                schema_metadata = EXCLUDED.schema_metadata,
                department_id = EXCLUDED.department_id,
                hero_image_url = EXCLUDED.hero_image_url,
                source_project_id = EXCLUDED.source_project_id,
                content_role = EXCLUDED.content_role,
                source_pillar_slug = EXCLUDED.source_pillar_slug,
                updated_at = NOW()
            RETURNING id
            """;

        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        parameters.Add("LanguageCode", command.LanguageCode);
        parameters.Add("Slug", command.Slug);
        parameters.Add("Title", command.Title);
        parameters.Add("Body", command.Body);
        parameters.Add("PostType", command.PostType);
        parameters.Add("SchemaMetadata", command.SchemaMetadataJson);
        parameters.Add("DepartmentId", departmentId);
        parameters.Add("HeroImageUrl", command.HeroImageUrl);
        parameters.Add("SourceProjectId", command.SourceProjectId);
        parameters.Add("ContentRole", command.ContentRole);
        parameters.Add("SourcePillarSlug", command.SourcePillarSlug);

        var translationId = await _ambient.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, parameters, _ambient.Transaction, cancellationToken: ct));

        await ReplacePresentationAsync(translationId, command.Presentation, ct);
    }

    private async Task<int?> ResolveDepartmentIdAsync(UpsertBlogPostCommand command, CancellationToken ct)
    {
        var departmentSlug = command.DepartmentSlug?.Trim();
        if (string.IsNullOrEmpty(departmentSlug))
        {
            var parts = command.Slug.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
                departmentSlug = parts[1];
        }

        if (string.IsNullOrEmpty(departmentSlug))
            return null;

        const string sql = """
            SELECT id
            FROM public.departments
            WHERE slug = @Slug
            LIMIT 1
            """;

        return await _ambient.Connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                sql,
                new { Slug = departmentSlug },
                _ambient.Transaction,
                cancellationToken: ct));
    }

    private async Task ReplacePresentationAsync(
        int translationId,
        IReadOnlyDictionary<string, string>? presentation,
        CancellationToken ct)
    {
        await _ambient.Connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM geek_blog.post_presentation WHERE post_translation_id = @TranslationId",
                new { TranslationId = translationId },
                _ambient.Transaction,
                cancellationToken: ct));

        var normalized = PostPresentationFields.Normalize(presentation);
        foreach (var (surface, copy) in normalized)
        {
            const string insertSql = """
                INSERT INTO geek_blog.post_presentation (post_translation_id, surface, copy)
                VALUES (@TranslationId, @Surface, @Copy)
                """;

            await _ambient.Connection.ExecuteAsync(
                new CommandDefinition(
                    insertSql,
                    new { TranslationId = translationId, Surface = surface, Copy = copy },
                    _ambient.Transaction,
                    cancellationToken: ct));
        }
    }

    private async Task ReplacePostTagsAsync(
        int postId,
        string languageCode,
        IReadOnlyList<string> tagSlugs,
        CancellationToken ct)
    {
        await _ambient.Connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM geek_blog.post_tags WHERE post_id = @PostId",
                new { PostId = postId },
                _ambient.Transaction,
                cancellationToken: ct));

        foreach (var tagSlug in tagSlugs.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            const string upsertTagSql = """
                INSERT INTO geek_blog.tags (slug)
                VALUES (@Slug)
                ON CONFLICT (slug) DO UPDATE SET slug = EXCLUDED.slug
                RETURNING id
                """;

            var tagId = await _ambient.Connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    upsertTagSql,
                    new { Slug = tagSlug },
                    _ambient.Transaction,
                    cancellationToken: ct));

            const string upsertTranslationSql = """
                INSERT INTO geek_blog.tag_translations (tag_id, language_code, name)
                VALUES (@TagId, @LanguageCode, @Name)
                ON CONFLICT (tag_id, language_code) DO UPDATE SET name = EXCLUDED.name
                """;

            await _ambient.Connection.ExecuteAsync(
                new CommandDefinition(
                    upsertTranslationSql,
                    new { TagId = tagId, LanguageCode = languageCode, Name = HumanizeTagSlug(tagSlug) },
                    _ambient.Transaction,
                    cancellationToken: ct));

            await _ambient.Connection.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO geek_blog.post_tags (post_id, tag_id) VALUES (@PostId, @TagId) ON CONFLICT DO NOTHING",
                    new { PostId = postId, TagId = tagId },
                    _ambient.Transaction,
                    cancellationToken: ct));
        }
    }

    private static string HumanizeTagSlug(string slug) =>
        string.Join(' ', slug.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
