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

    private const string SelectColumns = """
            p.id              AS PostId,
            p.post_type       AS PostType,
            pt.language_code  AS LanguageCode,
            p.slug::text      AS Slug,
            pt.title          AS Title,
            p.is_published    AS IsPublished,
            p.schema_type     AS SchemaType,
            p.published_at    AS PublishedAt,
            p.created_at      AS CreatedAt,
            p.updated_at      AS UpdatedAt,
            COALESCE(tags.localized_tags_json, '[]') AS LocalizedTagsJson,
            p.category_id     AS CategoryId,
            c.slug            AS CategorySlug,
            pt.summary        AS Summary,
            pt.meta_description AS MetaDescription,
            pt.json_ld_override AS JsonLdOverride,
            p.cw_job_id       AS CwJobId,
            COALESCE(sections.sections_json, '[]') AS SectionsJson,
            COALESCE(pres.presentation_json, '{}') AS PresentationJson
        """;

    private const string JoinClauses = """
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
            LEFT JOIN geek_blog.categories c ON c.id = p.category_id
            LEFT JOIN LATERAL (
                SELECT COALESCE(json_agg(
                    json_build_object(
                        'sortOrder', ps.sort_order,
                        'headingTag', ps.heading_tag,
                        'headingText', ps.heading_text,
                        'bodyContent', ps.body_content,
                        'mediaUrl', ps.media_url,
                        'mediaAlt', ps.media_alt
                    ) ORDER BY ps.sort_order
                ), '[]')::text AS sections_json
                FROM geek_blog.post_sections ps
                WHERE ps.post_translation_id = pt.id
            ) sections ON TRUE
            LEFT JOIN LATERAL (
                SELECT COALESCE(
                    json_object_agg(ppa.attribute_key, ppa.attribute_value) FILTER (WHERE ppa.attribute_key IS NOT NULL),
                    '{}'
                )::text AS presentation_json
                FROM geek_blog.post_presentation_attributes ppa
                WHERE ppa.post_translation_id = pt.id
            ) pres ON TRUE
        """;

    public async Task<bool> UserHasRoleAsync(
        int userId,
        string roleName,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM geek_blog.user_roles ur
                INNER JOIN geek_blog.roles r ON r.id = ur.role_id
                WHERE ur.user_id = @UserId
                  AND r.name = @RoleName
            )
            """;

        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("RoleName", roleName);

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
        var sql = $"""
            SELECT
                {SelectColumns},
                ts_rank(pt.search_vector, websearch_to_tsquery(geek_blog.resolve_ts_config(@LanguageCode), @SearchTerm)) AS SearchRank
            FROM geek_blog.post_translations pt
            INNER JOIN geek_blog.posts p ON p.id = pt.post_id
            {JoinClauses}
            WHERE pt.language_code = @LanguageCode
              AND p.is_published = TRUE
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
        var sql = $"""
            SELECT
                {SelectColumns}
            FROM geek_blog.post_translations pt
            INNER JOIN geek_blog.posts p ON p.id = pt.post_id
            {JoinClauses}
            WHERE p.slug = @Slug
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
        var sql = $"""
            SELECT
                {SelectColumns}
            FROM geek_blog.posts p
            INNER JOIN geek_blog.post_translations pt
                ON pt.post_id = p.id AND pt.language_code = @LanguageCode
            {JoinClauses}
            WHERE p.schema_type = 'TechnicalArticle'
              AND p.is_published = TRUE
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
                c.id             AS Id,
                c.post_id        AS PostId,
                c.user_id        AS UserId,
                c.content        AS Content,
                c.attachment_url AS AttachmentUrl,
                c.path::text     AS Path,
                nlevel(c.path)   AS Depth,
                c.created_at     AS CreatedAt
            FROM geek_blog.post_comments c
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
        string? attachmentUrl,
        CancellationToken ct = default)
    {
        const string insertSql = """
            WITH new_id AS (
                SELECT nextval(pg_get_serial_sequence('geek_blog.post_comments', 'id')) AS id
            )
            INSERT INTO geek_blog.post_comments (id, post_id, user_id, content, attachment_url, path)
            SELECT
                new_id.id,
                @PostId,
                @UserId,
                @Content,
                @AttachmentUrl,
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
        parameters.Add("AttachmentUrl", attachmentUrl);
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
        var sql = $"""
            SELECT
                {SelectColumns}
            FROM geek_blog.posts p
            INNER JOIN geek_blog.post_translations pt ON pt.post_id = p.id
            {JoinClauses}
            WHERE 1=1
            """;

        bool? isPublished = status?.Trim().ToLowerInvariant() switch
        {
            "published" => true,
            "draft" => false,
            _ => null,
        };

        if (languageCode is not null)
            sql += " AND pt.language_code = @LanguageCode";
        if (isPublished is not null)
            sql += " AND p.is_published = @IsPublished";
        if (postType is not null)
            sql += " AND p.post_type = @PostType::geek_blog.post_type_enum";

        sql += " ORDER BY p.updated_at DESC, p.id DESC";

        var parameters = new DynamicParameters();
        if (languageCode is not null) parameters.Add("LanguageCode", languageCode);
        if (isPublished is not null) parameters.Add("IsPublished", isPublished);
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
        var sql = $"""
            SELECT
                {SelectColumns}
            FROM geek_blog.posts p
            INNER JOIN geek_blog.post_translations pt ON pt.post_id = p.id
            {JoinClauses}
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

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(
        string? languageCode = null,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                c.id   AS Id,
                c.slug AS Slug,
                ct.name AS Name
            FROM geek_blog.categories c
            LEFT JOIN geek_blog.category_translations ct
                ON ct.category_id = c.id AND ct.language_code = @LanguageCode
            ORDER BY c.slug
            """;

        var command = new CommandDefinition(
            sql,
            new { LanguageCode = languageCode ?? "en" },
            _ambient.Transaction,
            cancellationToken: ct);

        var rows = await _ambient.Connection.QueryAsync<CategoryDto>(command);
        return rows.ToList();
    }

    public async Task<int> CreatePostAsync(UpsertBlogPostCommand command, CancellationToken ct = default)
    {
        var categoryId = await ResolveCategoryIdAsync(command.CategorySlug, ct);
        var publishedAt = ResolvePublishedAt(command);

        const string insertPostSql = """
            INSERT INTO geek_blog.posts (slug, post_type, schema_type, category_id, author_id, cw_job_id, is_published, published_at)
            VALUES (
                @Slug,
                @PostType::geek_blog.post_type_enum,
                @SchemaType::geek_blog.schema_type_enum,
                @CategoryId,
                @AuthorId,
                @CwJobId,
                @IsPublished,
                @PublishedAt)
            RETURNING id
            """;

        var postParams = new DynamicParameters();
        postParams.Add("Slug", command.Slug);
        postParams.Add("PostType", command.PostType);
        postParams.Add("SchemaType", command.SchemaType);
        postParams.Add("CategoryId", categoryId);
        postParams.Add("AuthorId", command.AuthorId);
        postParams.Add("CwJobId", command.CwJobId);
        postParams.Add("IsPublished", command.IsPublished);
        postParams.Add("PublishedAt", publishedAt);

        var postId = await _ambient.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(insertPostSql, postParams, _ambient.Transaction, cancellationToken: ct));

        var translationId = await UpsertTranslationAsync(postId, categoryId, command, ct);
        await ReplaceSectionsAsync(translationId, command.Sections, ct);
        await ReplacePostTagsAsync(postId, command.TagSlugs, ct);

        return postId;
    }

    public async Task<bool> UpdatePostAsync(int postId, UpsertBlogPostCommand command, CancellationToken ct = default)
    {
        const string existsSql = "SELECT EXISTS(SELECT 1 FROM geek_blog.posts WHERE id = @PostId)";
        var exists = await _ambient.Connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(existsSql, new { PostId = postId }, _ambient.Transaction, cancellationToken: ct));
        if (!exists) return false;

        var categoryId = await ResolveCategoryIdAsync(command.CategorySlug, ct);
        var publishedAt = ResolvePublishedAt(command);

        const string updatePostSql = """
            UPDATE geek_blog.posts
            SET slug = @Slug,
                post_type = @PostType::geek_blog.post_type_enum,
                schema_type = @SchemaType::geek_blog.schema_type_enum,
                category_id = @CategoryId,
                author_id = @AuthorId,
                cw_job_id = @CwJobId,
                is_published = @IsPublished,
                published_at = @PublishedAt
            WHERE id = @PostId
            """;

        var postParams = new DynamicParameters();
        postParams.Add("PostId", postId);
        postParams.Add("Slug", command.Slug);
        postParams.Add("PostType", command.PostType);
        postParams.Add("SchemaType", command.SchemaType);
        postParams.Add("CategoryId", categoryId);
        postParams.Add("AuthorId", command.AuthorId);
        postParams.Add("CwJobId", command.CwJobId);
        postParams.Add("IsPublished", command.IsPublished);
        postParams.Add("PublishedAt", publishedAt);

        await _ambient.Connection.ExecuteAsync(
            new CommandDefinition(updatePostSql, postParams, _ambient.Transaction, cancellationToken: ct));

        var translationId = await UpsertTranslationAsync(postId, categoryId, command, ct);
        await ReplaceSectionsAsync(translationId, command.Sections, ct);
        await ReplacePostTagsAsync(postId, command.TagSlugs, ct);

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
        command.IsPublished
            ? (command.PublishedAt ?? DateTimeOffset.UtcNow).ToUniversalTime()
            : null;

    private async Task<int> UpsertTranslationAsync(
        int postId,
        int categoryId,
        UpsertBlogPostCommand command,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO geek_blog.post_translations
                (post_id, language_code, title, summary, meta_description, json_ld_override)
            VALUES
                (@PostId, @LanguageCode, @Title, @Summary, @MetaDescription, @JsonLdOverride)
            ON CONFLICT (post_id, language_code) DO UPDATE SET
                title = EXCLUDED.title,
                summary = EXCLUDED.summary,
                meta_description = EXCLUDED.meta_description,
                json_ld_override = EXCLUDED.json_ld_override
            RETURNING id
            """;

        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        parameters.Add("LanguageCode", command.LanguageCode);
        parameters.Add("Title", command.Title);
        parameters.Add("Summary", command.Summary);
        parameters.Add("MetaDescription", command.MetaDescription);
        parameters.Add("JsonLdOverride", command.JsonLdOverride);

        var translationId = await _ambient.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, parameters, _ambient.Transaction, cancellationToken: ct));

        await ReplacePresentationAsync(translationId, command.Presentation, ct);

        return translationId;
    }

    private async Task<int> ResolveCategoryIdAsync(string categorySlug, CancellationToken ct)
    {
        var slug = categorySlug?.Trim();
        if (string.IsNullOrEmpty(slug))
            throw new InvalidOperationException("CategorySlug is required to create or update a blog post.");

        const string sql = "SELECT id FROM geek_blog.categories WHERE slug = @Slug";

        var categoryId = await _ambient.Connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                sql,
                new { Slug = slug },
                _ambient.Transaction,
                cancellationToken: ct));

        return categoryId ?? throw new InvalidOperationException(
            $"Unknown category slug '{slug}' — no matching row in geek_blog.categories. " +
            "Categories are a fixed taxonomy; add it there first if it's meant to be a real category.");
    }

    private async Task ReplaceSectionsAsync(
        int translationId,
        IReadOnlyList<PostSectionInput> sections,
        CancellationToken ct)
    {
        await _ambient.Connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM geek_blog.post_sections WHERE post_translation_id = @TranslationId",
                new { TranslationId = translationId },
                _ambient.Transaction,
                cancellationToken: ct));

        foreach (var section in sections)
        {
            const string insertSql = """
                INSERT INTO geek_blog.post_sections
                    (post_translation_id, sort_order, heading_tag, heading_text, body_content, media_url, media_alt)
                VALUES
                    (@TranslationId, @SortOrder, @HeadingTag, @HeadingText, @BodyContent, @MediaUrl, @MediaAlt)
                """;

            await _ambient.Connection.ExecuteAsync(
                new CommandDefinition(
                    insertSql,
                    new
                    {
                        TranslationId = translationId,
                        section.SortOrder,
                        section.HeadingTag,
                        section.HeadingText,
                        section.BodyContent,
                        section.MediaUrl,
                        section.MediaAlt,
                    },
                    _ambient.Transaction,
                    cancellationToken: ct));
        }
    }

    private async Task ReplacePresentationAsync(
        int translationId,
        IReadOnlyDictionary<string, string>? presentation,
        CancellationToken ct)
    {
        await _ambient.Connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM geek_blog.post_presentation_attributes WHERE post_translation_id = @TranslationId",
                new { TranslationId = translationId },
                _ambient.Transaction,
                cancellationToken: ct));

        var normalized = PostPresentationFields.Normalize(presentation);
        foreach (var (key, value) in normalized)
        {
            const string insertSql = """
                INSERT INTO geek_blog.post_presentation_attributes (post_translation_id, attribute_key, attribute_value)
                VALUES (@TranslationId, @AttributeKey, @AttributeValue)
                """;

            await _ambient.Connection.ExecuteAsync(
                new CommandDefinition(
                    insertSql,
                    new { TranslationId = translationId, AttributeKey = key, AttributeValue = value },
                    _ambient.Transaction,
                    cancellationToken: ct));
        }
    }

    private async Task ReplacePostTagsAsync(
        int postId,
        IReadOnlyList<string> tagSlugs,
        CancellationToken ct)
    {
        await _ambient.Connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM geek_blog.post_tags WHERE post_id = @PostId",
                new { PostId = postId },
                _ambient.Transaction,
                cancellationToken: ct));

        const string lookupTagSql = "SELECT id FROM geek_blog.tags WHERE slug = @Slug";

        foreach (var tagSlug in tagSlugs.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var tagId = await _ambient.Connection.ExecuteScalarAsync<int?>(
                new CommandDefinition(
                    lookupTagSql,
                    new { Slug = tagSlug },
                    _ambient.Transaction,
                    cancellationToken: ct));

            // Unrecognized tags are skipped, not auto-created — geek_blog.tags is curated by hand.
            if (tagId is null)
                continue;

            await _ambient.Connection.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO geek_blog.post_tags (post_id, tag_id) VALUES (@PostId, @TagId) ON CONFLICT DO NOTHING",
                    new { PostId = postId, TagId = tagId.Value },
                    _ambient.Transaction,
                    cancellationToken: ct));
        }
    }
}
