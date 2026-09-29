# GeekAPI

GeekAPI is the **platform API gateway** for Geek apps. It proxies internal product data to GeekRepository, exposes **public content** APIs, and health checks. It does **not** host login or legacy `/api/auth/*` (retired May 2026 — use **GeekOAuth**).

## Routes (current)

| Area | Path |
|------|------|
| GTM data pipe | `/api/gtm/internal/*` → `repo/gtm/*` on GeekRepository |
| Content (public read) | `/api/case-studies`, `/api/departments`, `/api/use-cases` |
| Health | `/health`, `/hello` |

`/api/seo/internal/*` still resolves, but its GeekRepository half was deleted on 2026-09-29 — every
path under it now relays a bare 404. It is not a live pipe; see `AGENTS.md`.

## Retired (410 Gone)

| Area | Former path |
|------|-------------|
| Auth storage | `/api/auth/*` |
| SignalR sync | `/hubs/sync` |

## Environment

| Variable | Purpose |
|----------|---------|
| `REPO_URL` | GeekRepository base URL |
| `REPO_API_KEY` | Machine auth to repository |
| `GEEK_BACKEND_API_KEY` | Client API key + SEO internal proxy |
| `CORS_ORIGINS` | Allowed browser origins |
| `PORT` | Listen port (default 8080) |

## Build / run

```bash
dotnet run --project GeekAPI
```

**Railway:** `./Dockerfile` (not `Dockerfile.repository`).
