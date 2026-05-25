# Geek SEO database schema (`geek_seo`)

GeekSeoBackend (`Geek-SEO/GeekSeoBackend`) has **no database connection**. All persistence goes through GeekRepository HTTP routes under `repo/seo/*`, backed by PostgreSQL schema **`geek_seo`**.

GeekAPI is **not** the SEO product API — it only issues OAuth tokens (including client `geekseo-backend` for repository access). See [Geek-SEO/plan-documents/ARCHITECTURE.md](https://github.com/jmartinemployment/Geek-SEO/blob/main/plan-documents/ARCHITECTURE.md).

## Schema and migrations

| Item | Location |
|------|----------|
| EF Core context | `GeekRepository/Data/SeoDbContext.cs` |
| Initial migration | `GeekRepository/Migrations/Seo/20260519135803_InitialSeoSchema.cs` |
| History table | `geek_seo.__EFSeoMigrationsHistory` (separate from AppDbContext history) |
| Apply at startup | `GeekRepository/Program.cs` → `ApplySeoMigrationsAsync` (uses `DATABASE_URL`) |

Local CLI:

```bash
dotnet ef database update --context SeoDbContext --project GeekRepository --startup-project GeekRepository
```

### Production (Railway GeekRepository)

1. Ensure `DATABASE_URL` is set (migrations run with this principal).
2. Optional: `GEEK_SEO_DATABASE_URL` for runtime-only `geekseo_app` role — see `scripts/geek_seo_db_setup.sql`.
3. Optional: `GEEK_SEO_ENCRYPTION_KEY` (base64 32-byte key) for WordPress credential encryption.
4. Redeploy after migration fixes; verify logs contain `Geek SEO schema migrations applied successfully.`

Migrations run without `UseSnakeCaseNamingConvention` so `geek_seo.__EFSeoMigrationsHistory` uses `MigrationId` / `ProductVersion` columns. Runtime `SeoDbContext` still uses snake_case for data tables only.

## MVP tables (GeekSeoBackend → GeekRepository today)

| Table | GeekRepository route | GeekSeoBackend usage |
|-------|----------------------|----------------------|
| `seo_projects` | `repo/seo/projects` | CRUD projects |
| `seo_content_documents` | `repo/seo/content` | Editor documents, scoring updates |
| `seo_background_jobs` | `repo/seo/jobs` | Job status polling |
| `seo_subscriptions` | `repo/seo/subscriptions` | Feature gates (null row → Starter tier) |
| `seo_usage_counters` | `repo/seo/usage` | Usage metering |
| `seo_serp_results`, `seo_serp_deep_cache`, `seo_competitor_pages` | `repo/seo/serp-cache`, content competitors | SERP / competitor crawl |
| `seo_keywords`, `seo_keyword_clusters` | `repo/seo/keywords` | Research / clustering |
| `seo_wordpress_connections`, `seo_published_pages` | `repo/seo/wordpress` | WordPress publish |
| — | `repo/seo/briefs/generate` | AI brief (no table; ephemeral) |
| — | `repo/seo/writing/*` | AI writing (jobs written in `seo_background_jobs`) |
| — | `repo/seo/scoring/*` | Real-time scoring pipeline |

## Additional tables (same migration; future Geek SEO features)

`seo_alerts`, `seo_api_keys`, `seo_brand_voices`, `seo_bulk_jobs`, `seo_cannibalization_issues`, `seo_ga4_connections`, `seo_gsc_connections`, `seo_rank_tracking`, `seo_page_audits`, `seo_site_audits`, `seo_site_audit_pages`, `seo_reports`, `seo_topical_maps`, `seo_site_page_inventory`, `seo_plagiarism_checks`, `seo_geo_tracking_queries`, `seo_geo_mention_snapshots`, `seo_organizations`, `seo_organization_members`, `seo_content_performance_snapshots`

## Plan-only (not in EF migration yet)

From product plan, not required for current GeekSeoBackend HTTP surface:

- `seo_content_briefs` (persisted briefs)
- `seo_gsc_rankings` (separate from `seo_rank_tracking`)
- `seo_published_page_audits`, `seo_content_guard_policies`, `seo_content_guard_runs`

## OAuth clients (GeekAPI issuer)

| Client ID | Secret env | Calls |
|-----------|------------|-------|
| `geekapi` | `GEEK_API_CLIENT_SECRET` | GeekAPI → GeekRepository (auth + content EF) |
| `geekseo-backend` | `GEEK_SEO_BACKEND_CLIENT_SECRET` | GeekSeoBackend → GeekRepository (`repo/seo/*`) |
| `geek-seo-electron` | — (public + PKCE) | Desktop app → GeekAPI `/connect/*` only |
