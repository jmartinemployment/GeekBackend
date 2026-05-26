# GeekAPI — platform API gateway

GeekAPI is the **platform API gateway** for Geek apps. It exposes auth storage APIs (`/api/auth/*`), public content APIs, and SignalR sync. It does **not** host an authorization server.

Persistence goes to **GeekRepository** over HTTP (`REPO_URL`). See [Architecture.md](../Architecture.md) for S2S auth: **target = short-lived JWT** (`geekapi` + `internal.api`); **`REPO_API_KEY` is interim/dev only**, not production security.

Trusted callers use `X-API-Key` (`GEEK_BACKEND_API_KEY`); auth routes may also send `X-Geek-User-Id` for user-scoped operations.

## Key routes

| Area | Path prefix |
|------|-------------|
| Auth storage | `/api/auth/*` |
| SEO data gateway | `/api/seo/internal/*` → GeekRepository `repo/seo/*` (`GEEK_BACKEND_API_KEY`) |
| Content | `/api/case-studies`, `/api/departments`, `/api/use-cases` |
| Sync | `/hubs/sync` |

## Environment

| Variable | Purpose |
|----------|---------|
| `REPO_URL` | GeekRepository base URL |
| `GEEK_BACKEND_API_KEY` | `X-API-Key` for inbound API calls |
| `CORS_ORIGINS` | Allowed browser origins |
| `PORT` | Listen port (default 8080) |
| `REPO_API_KEY` | **Interim only** — local/CI; remove when JWT S2S is wired |

## Session notes

**May 24, 2026:** Removed OpenIddict issuer and Geek SEO product surface from GeekAPI. Platform tokens belong on external auth at `auth.geekatyourspot.com`. Repo access must move off shared `REPO_API_KEY` to client-credentials JWT.
