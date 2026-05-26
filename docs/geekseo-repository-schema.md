# Geek SEO database schema (`geek_seo`)

GeekSeoBackend (`Geek-SEO/GeekSeoBackend`) has **no database connection**. Persistence flows:

**GeekSeoBackend** → **GeekAPI** `api/seo/internal/*` → **GeekRepository** `repo/seo/*` → PostgreSQL schema **`geek_seo`**.

Only GeekAPI holds `REPO_URL` / `REPO_API_KEY`. GeekSeoBackend uses `GEEK_API_URL` and `GEEK_BACKEND_API_KEY` (same key as other trusted platform callers).

## Schema and migrations

| Item | Location |
|------|----------|
| EF Core context | `GeekRepository/Data/SeoDbContext.cs` |
| Initial migration | `GeekRepository/Migrations/Seo/20260519135803_InitialSeoSchema.cs` |
| History table | `geek_seo.__EFSeoMigrationsHistory` |
| Apply at startup | `GeekRepository/Program.cs` → `ApplySeoMigrationsAsync` (`DATABASE_URL`) |

Local CLI:

```bash
dotnet ef database update --context SeoDbContext --project GeekRepository --startup-project GeekRepository
```

### Production (Railway)

1. **GeekRepository:** `DATABASE_URL` set; redeploy and confirm log: `Geek SEO (geek_seo) schema migrations applied successfully.`
2. **GeekAPI:** `REPO_URL`, `REPO_API_KEY`, `GEEK_BACKEND_API_KEY` set; `REPO_API_KEY` on both services must match.
3. **GeekSeoBackend:** `GEEK_API_URL` → GeekAPI public URL; `GEEK_BACKEND_API_KEY` → same value as GeekAPI inbound key.
4. Optional: `GEEK_SEO_ENCRYPTION_KEY` on **GeekSeoBackend** (WordPress credential encryption at product layer).

Migration `0004_drop_oauth_openiddict_and_adapter_storage.sql` removes issuer-era tables but **does not** drop `geek_seo`.

## Repository routes (data only)

| Route | Purpose |
|-------|---------|
| `repo/seo/projects` | Projects CRUD |
| `repo/seo/content` | Content documents |
| `repo/seo/jobs` | Background job records |
| `repo/seo/subscriptions` | Subscription tier row |
| `repo/seo/usage` | Usage counters |
| `repo/seo/serp-cache` | SERP cache rows |
| `repo/seo/competitor-pages` | Competitor page crawl storage |
| `repo/seo/keywords` | Keyword research storage |
| `repo/seo/wordpress/connections` | WordPress connection rows |
| `repo/seo/wordpress/publish-log` | Publish history |
| `repo/seo/brand-voices` | Brand voice profiles |

AI, SERP providers, Playwright, and scoring logic run on **GeekSeoBackend** only — not on GeekRepository.

## GeekAPI internal gateway

| GeekSeoBackend calls | GeekAPI proxies to |
|----------------------|-------------------|
| `api/seo/internal/{path}` | `repo/seo/{path}` |

Controller: `GeekAPI/Controllers/Seo/SeoInternalProxyController.cs`. Auth: `X-API-Key` (`GEEK_BACKEND_API_KEY`) plus `X-Geek-User-Id` or `userId` query for row scope.
