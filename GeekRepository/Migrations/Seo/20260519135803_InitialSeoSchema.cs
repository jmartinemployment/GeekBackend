using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Migrations.Seo
{
    /// <inheritdoc />
    public partial class InitialSeoSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            migrationBuilder.EnsureSchema(
                name: "geek_seo");

            migrationBuilder.CreateTable(
                name: "seo_alerts",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    alert_type = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_alerts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_api_keys",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_hash = table.Column<string>(type: "text", nullable: false),
                    key_prefix = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_api_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_background_jobs",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    progress_percent = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_background_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_brand_voices",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sample_text = table.Column<string>(type: "text", nullable: false),
                    style_instructions = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_brand_voices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_bulk_jobs",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    keywords_json = table.Column<string>(type: "text", nullable: false),
                    completed_count = table.Column<int>(type: "integer", nullable: false),
                    total_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_bulk_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_cannibalization_issues",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    keyword = table.Column<string>(type: "text", nullable: false),
                    competing_urls_json = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "text", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_cannibalization_issues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_ga4_connections",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<string>(type: "text", nullable: false),
                    encrypted_refresh_token = table.Column<byte[]>(type: "bytea", nullable: false),
                    encryption_iv = table.Column<byte[]>(type: "bytea", nullable: false),
                    encryption_tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_ga4_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_geo_tracking_queries",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query_text = table.Column<string>(type: "text", nullable: false),
                    platforms_json = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_geo_tracking_queries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_gsc_connections",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_url = table.Column<string>(type: "text", nullable: false),
                    encrypted_refresh_token = table.Column<byte[]>(type: "bytea", nullable: false),
                    encryption_iv = table.Column<byte[]>(type: "bytea", nullable: false),
                    encryption_tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_gsc_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_keyword_clusters",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    pillar_keyword = table.Column<string>(type: "text", nullable: false),
                    keywords_json = table.Column<string>(type: "text", nullable: false),
                    average_volume = table.Column<int>(type: "integer", nullable: false),
                    average_difficulty = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_keyword_clusters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_keywords",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cluster_id = table.Column<Guid>(type: "uuid", nullable: true),
                    keyword = table.Column<string>(type: "text", nullable: false),
                    location = table.Column<string>(type: "text", nullable: false),
                    search_volume = table.Column<int>(type: "integer", nullable: true),
                    keyword_difficulty = table.Column<decimal>(type: "numeric", nullable: true),
                    intent = table.Column<string>(type: "text", nullable: true),
                    cached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_keywords", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_organizations",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_organizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_page_audits",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    issues_json = table.Column<string>(type: "text", nullable: false),
                    metadata_json = table.Column<string>(type: "text", nullable: false),
                    audited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_page_audits", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_plagiarism_checks",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_percent = table.Column<decimal>(type: "numeric", nullable: false),
                    matches_json = table.Column<string>(type: "text", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_plagiarism_checks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_published_pages",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    url = table.Column<string>(type: "text", nullable: false),
                    word_press_post_id = table.Column<int>(type: "integer", nullable: true),
                    target_keyword = table.Column<string>(type: "text", nullable: true),
                    last_audit_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_published_pages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_rank_tracking",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    keyword = table.Column<string>(type: "text", nullable: false),
                    page_url = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<decimal>(type: "numeric", nullable: false),
                    impressions = table.Column<int>(type: "integer", nullable: false),
                    clicks = table.Column<int>(type: "integer", nullable: false),
                    ctr = table.Column<decimal>(type: "numeric", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_rank_tracking", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_reports",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_type = table.Column<string>(type: "text", nullable: false),
                    storage_path = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_serp_deep_cache",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    keyword = table.Column<string>(type: "text", nullable: false),
                    location = table.Column<string>(type: "text", nullable: false),
                    result_count = table.Column<int>(type: "integer", nullable: false),
                    results_json = table.Column<string>(type: "text", nullable: false),
                    term_matrix_json = table.Column<string>(type: "text", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_serp_deep_cache", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_serp_results",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    keyword = table.Column<string>(type: "text", nullable: false),
                    location = table.Column<string>(type: "text", nullable: false),
                    language_code = table.Column<string>(type: "text", nullable: false),
                    results_json = table.Column<string>(type: "text", nullable: false),
                    people_also_ask_json = table.Column<string>(type: "text", nullable: false),
                    related_searches_json = table.Column<string>(type: "text", nullable: false),
                    featured_snippet = table.Column<string>(type: "text", nullable: true),
                    serp_features_json = table.Column<string>(type: "text", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_serp_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_site_audits",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    pages_crawled = table.Column<int>(type: "integer", nullable: false),
                    overall_score = table.Column<decimal>(type: "numeric", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_site_audits", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_site_page_inventory",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    h1 = table.Column<string>(type: "text", nullable: true),
                    word_count = table.Column<int>(type: "integer", nullable: false),
                    crawled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_site_page_inventory", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_subscriptions",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<string>(type: "text", nullable: false),
                    paypal_subscription_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    current_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_topical_maps",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    clusters_json = table.Column<string>(type: "text", nullable: false),
                    content_gaps_json = table.Column<string>(type: "text", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_topical_maps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_usage_counters",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    feature = table.Column<string>(type: "text", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_usage_counters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_wordpress_connections",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_url = table.Column<string>(type: "text", nullable: false),
                    username = table.Column<string>(type: "text", nullable: false),
                    encrypted_app_password = table.Column<byte[]>(type: "bytea", nullable: false),
                    encryption_iv = table.Column<byte[]>(type: "bytea", nullable: false),
                    encryption_tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    default_post_status = table.Column<string>(type: "text", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_wordpress_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seo_geo_mention_snapshots",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    query_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    mentioned = table.Column<bool>(type: "boolean", nullable: false),
                    snippet = table.Column<string>(type: "text", nullable: true),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_geo_mention_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_seo_geo_mention_snapshots_seo_geo_tracking_queries_query_id",
                        column: x => x.query_id,
                        principalSchema: "geek_seo",
                        principalTable: "seo_geo_tracking_queries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seo_organization_members",
                schema: "geek_seo",
                columns: table => new
                {
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: true),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_organization_members", x => new { x.org_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_seo_organization_members_seo_organizations_org_id",
                        column: x => x.org_id,
                        principalSchema: "geek_seo",
                        principalTable: "seo_organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seo_projects",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    gsc_connected = table.Column<bool>(type: "boolean", nullable: false),
                    default_location = table.Column<string>(type: "text", nullable: false),
                    default_language = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_seo_projects_seo_organizations_org_id",
                        column: x => x.org_id,
                        principalSchema: "geek_seo",
                        principalTable: "seo_organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "seo_content_performance_snapshots",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    position = table.Column<decimal>(type: "numeric", nullable: true),
                    impressions = table.Column<int>(type: "integer", nullable: false),
                    clicks = table.Column<int>(type: "integer", nullable: false),
                    ctr = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_content_performance_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_seo_content_performance_snapshots_seo_published_pages_publi",
                        column: x => x.published_page_id,
                        principalSchema: "geek_seo",
                        principalTable: "seo_published_pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seo_competitor_pages",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    serp_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    domain = table.Column<string>(type: "text", nullable: true),
                    meta_title = table.Column<string>(type: "text", nullable: true),
                    meta_description = table.Column<string>(type: "text", nullable: true),
                    content_text = table.Column<string>(type: "text", nullable: false),
                    word_count = table.Column<int>(type: "integer", nullable: false),
                    headings_json = table.Column<string>(type: "text", nullable: false),
                    terms_json = table.Column<string>(type: "text", nullable: false),
                    internal_link_count = table.Column<int>(type: "integer", nullable: false),
                    external_link_count = table.Column<int>(type: "integer", nullable: false),
                    image_count = table.Column<int>(type: "integer", nullable: false),
                    images_missing_alt = table.Column<int>(type: "integer", nullable: false),
                    has_structured_data = table.Column<bool>(type: "boolean", nullable: false),
                    structured_data_types_json = table.Column<string>(type: "text", nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: false),
                    crawled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_competitor_pages", x => x.id);
                    table.ForeignKey(
                        name: "fk_seo_competitor_pages_seo_serp_results_serp_result_id",
                        column: x => x.serp_result_id,
                        principalSchema: "geek_seo",
                        principalTable: "seo_serp_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seo_site_audit_pages",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_audit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    issues_json = table.Column<string>(type: "text", nullable: false),
                    crawled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_site_audit_pages", x => x.id);
                    table.ForeignKey(
                        name: "fk_seo_site_audit_pages_seo_site_audits_site_audit_id",
                        column: x => x.site_audit_id,
                        principalSchema: "geek_seo",
                        principalTable: "seo_site_audits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seo_content_documents",
                schema: "geek_seo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    content_html = table.Column<string>(type: "text", nullable: false),
                    target_keyword = table.Column<string>(type: "text", nullable: false),
                    target_location = table.Column<string>(type: "text", nullable: false),
                    seo_score = table.Column<int>(type: "integer", nullable: false),
                    word_count = table.Column<int>(type: "integer", nullable: false),
                    score_components_json = table.Column<string>(type: "text", nullable: false),
                    last_scored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    published_score = table.Column<int>(type: "integer", nullable: true),
                    published_word_count = table.Column<int>(type: "integer", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ai_detection_score = table.Column<decimal>(type: "numeric", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_content_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_seo_content_documents_seo_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "geek_seo",
                        principalTable: "seo_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_seo_background_jobs_user_id_status",
                schema: "geek_seo",
                table: "seo_background_jobs",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_seo_competitor_pages_serp_result_id_url",
                schema: "geek_seo",
                table: "seo_competitor_pages",
                columns: new[] { "serp_result_id", "url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_content_documents_project_id",
                schema: "geek_seo",
                table: "seo_content_documents",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_seo_content_documents_status",
                schema: "geek_seo",
                table: "seo_content_documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_seo_content_documents_user_id",
                schema: "geek_seo",
                table: "seo_content_documents",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_seo_content_performance_snapshots_published_page_id_date",
                schema: "geek_seo",
                table: "seo_content_performance_snapshots",
                columns: new[] { "published_page_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_ga4_connections_project_id",
                schema: "geek_seo",
                table: "seo_ga4_connections",
                column: "project_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_geo_mention_snapshots_query_id",
                schema: "geek_seo",
                table: "seo_geo_mention_snapshots",
                column: "query_id");

            migrationBuilder.CreateIndex(
                name: "ix_seo_gsc_connections_project_id",
                schema: "geek_seo",
                table: "seo_gsc_connections",
                column: "project_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_organizations_slug",
                schema: "geek_seo",
                table: "seo_organizations",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_projects_org_id",
                schema: "geek_seo",
                table: "seo_projects",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "ix_seo_projects_user_id",
                schema: "geek_seo",
                table: "seo_projects",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_seo_published_pages_project_id_url",
                schema: "geek_seo",
                table: "seo_published_pages",
                columns: new[] { "project_id", "url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_serp_deep_cache_keyword_location_result_count",
                schema: "geek_seo",
                table: "seo_serp_deep_cache",
                columns: new[] { "keyword", "location", "result_count" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_serp_results_keyword_location_language_code",
                schema: "geek_seo",
                table: "seo_serp_results",
                columns: new[] { "keyword", "location", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_site_audit_pages_site_audit_id",
                schema: "geek_seo",
                table: "seo_site_audit_pages",
                column: "site_audit_id");

            migrationBuilder.CreateIndex(
                name: "ix_seo_site_page_inventory_project_id_url",
                schema: "geek_seo",
                table: "seo_site_page_inventory",
                columns: new[] { "project_id", "url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_subscriptions_user_id",
                schema: "geek_seo",
                table: "seo_subscriptions",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_usage_counters_user_id_period_start_feature",
                schema: "geek_seo",
                table: "seo_usage_counters",
                columns: new[] { "user_id", "period_start", "feature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_wordpress_connections_project_id",
                schema: "geek_seo",
                table: "seo_wordpress_connections",
                column: "project_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "seo_alerts",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_api_keys",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_background_jobs",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_brand_voices",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_bulk_jobs",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_cannibalization_issues",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_competitor_pages",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_content_documents",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_content_performance_snapshots",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_ga4_connections",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_geo_mention_snapshots",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_gsc_connections",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_keyword_clusters",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_keywords",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_organization_members",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_page_audits",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_plagiarism_checks",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_rank_tracking",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_reports",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_serp_deep_cache",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_site_audit_pages",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_site_page_inventory",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_subscriptions",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_topical_maps",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_usage_counters",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_wordpress_connections",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_serp_results",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_projects",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_published_pages",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_geo_tracking_queries",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_site_audits",
                schema: "geek_seo");

            migrationBuilder.DropTable(
                name: "seo_organizations",
                schema: "geek_seo");
        }
    }
}
