# GeekBackend Architecture

GeekBackend is the **platform data gateway and content/SEO storage layer** for Geek products. It is **not** an authorization server. User login is **GeekOAuth** (`auth.geekatyourspot.com`). This repo ships two HTTP services and one shared contracts library.

**2026-05-30 (platform decoupling):** Legacy **GeekAPI `/api/auth/*`**, **SyncHub**, and **GeekRepository `repo/auth/*`** are **removed** from code; legacy **auth DB tables** dropped via migration `0006`. GeekAPI exposes **SEO internal proxy** (`/api/seo/internal/*`), **content** APIs, and health. Identity: **GeekOAuth**. See Geek-SEO `plan-documents/PLATFORM-DECOUPLING.md`.

---

## Platform topology

```mermaid
flowchart LR
  subgraph clients [Clients]
    Web[Web / WordPress]
    Desktop[Electron / mobile]
    S2S[Trusted backends]
  end

  subgraph auth [Outside this repo]
    AS[Authorization server<br/>auth.geekatyourspot.com]
  end

  subgraph geekbackend [GeekBackend.sln]
    API[GeekAPI<br/>api.geekatyourspot.com]
    REPO[GeekRepository<br/>data service]
    APP[GeekApplication<br/>contracts only]
  end

  DB[(PostgreSQL)]

  Web --> API
  Desktop --> AS
  Desktop --> API
  S2S --> API
  AS -->|adapter: auth storage| API
  API -->|HTTPS + machine JWT| REPO
  REPO --> DB
  API -.-> APP
  REPO -.-> APP
```

| Role (OAuth vocabulary) | Deployment | Responsibility |
|-------------------------|------------|----------------|
| **Authorization server** | `auth.geekatyourspot.com` (separate .NET service) | Issue tokens, OIDC discovery, login UI, client registry |
| **Resource server (platform API)** | GeekAPI | API-key gate; `/api/seo/internal/*` proxy to repo; public content reads; no legacy `/api/auth/*` |
| **Resource server (data)** | GeekRepository | All PostgreSQL reads/writes; no product-facing HTTP except from GeekAPI |
| **Client** | Each Geek product | OIDC against auth; **never** call GeekRepository directly |

**Hard rules**

1. **Only GeekAPI talks to GeekRepository** — no product service (OrderStack, RankPilot, etc.) holds `REPO_URL` or database credentials for platform auth/content.
2. **No authorization server in GeekBackend** — no `/connect/*`, no OpenIddict issuer on GeekAPI.
3. **Geek SEO product APIs** (AI, SERP, scoring, SignalR) live in **Geek-SEO / GeekSeoBackend**. **Geek SEO persistence** (`geek_seo` schema, `repo/seo/*`) lives in **GeekRepository**; GeekSeoBackend reaches it only via **GeekAPI** `api/seo/internal/*`.

---

## Solution layout

```
GEEKBACKEND.slnx
├── GeekApplication     Class library — entities, interfaces, Result<T>, auth helpers (no HTTP, no DB)
├── GeekAPI             Web app — platform gateway (Railway: api.geekatyourspot.com)
└── GeekRepository      Web app — universal data service (Railway: GeekRepository project)
```

### Project references (compile-time)

```
GeekAPI        → GeekApplication only
GeekRepository → GeekApplication only
GeekApplication → (no project references)
```

GeekAPI **must not** reference GeekRepository. All data access is **HTTP** from GeekAPI HttpClients to GeekRepository controllers.

---

## GeekAPI (today)

**Purpose:** Platform API **gateway** — SEO internal proxy, public content reads, health. **Not** an authorization server; **not** legacy auth storage (retired May 2026).

| Surface | Auth | Notes |
|---------|------|--------|
| `/api/seo/internal/*` | `X-API-Key` + optional user headers | Proxies to `repo/seo/*` on GeekRepository |
| `/api/case-studies`, `/api/departments`, `/api/use-cases` | Public read | Marketing/content |
| `/api/auth/*`, `/hubs/sync` | — | **410 Gone** (use GeekOAuth) |

**Outbound:** `HttpClient` `GeekRepository` → `REPO_URL` with `REPO_API_KEY` (interim; JWT S2S is target).

**Does not:** Issue tokens, host login UI, or run Geek SEO product logic.

---

## GeekRepository (today)

**Purpose:** Data plane for **`geek_seo`** (product schema) and **shared content** tables. No legacy platform auth tables after migration `0006`.

| Access style | Tables | Tooling |
|--------------|--------|---------|
| EF (`SeoDbContext`) | `geek_seo.*` | Migrations from **GeekSeo.Persistence** (Geek-SEO repo) |
| EF (`AppDbContext`) | `departments`, `case_studies`, `use_cases`, … | Platform content migrations |

Controllers use **InternalService** policy (`REPO_API_KEY` / `internal.api` scope).

---

## Service-to-service auth: today vs target

Shared static secrets (`REPO_API_KEY` / `X-Repo-Key`) are **not** the long-term design. They create a single long-lived bearer that, if leaked, grants full data-plane access with no audience binding, no rotation story, and no audit trail per call. Production incidents tied to bootstrap secrets are why this path is treated as **technical debt**.

### Target (zero-trust S2S)

| Property | Value |
|----------|--------|
| Grant | `client_credentials` |
| Client | `geekapi` (confidential), registered on **authorization server** |
| Token | Short-lived JWT |
| `aud` | `geek-repository` |
| Scope | `internal.api` |
| Transport | TLS only |
| Validation | GeekRepository validates JWT against auth issuer JWKS (`AUTH_SERVER_URL` → auth, not api) |

GeekAPI obtains a machine token from auth, attaches `Authorization: Bearer`, and GeekRepository validates issuer, audience, signature, expiry, and scope. **No shared repo API key in production.**

### Interim (local / integration tests only)

`REPO_API_KEY` exists in code today for bootstrap and tests. Treat it as:

- **Allowed:** local dev, CI with isolated databases, emergency break-glass with immediate rotation plan
- **Forbidden:** production reliance, documentation as “the” security model, or duplication across many Railway services

**Migration checklist**

1. Deploy authorization server with `geekapi` client + `internal.api` scope.
2. GeekRepository: JWT validation (issuer = auth), drop `RepoApiKeyAuthenticationHandler` from production config.
3. GeekAPI: client-credentials token provider + delegating handler on `GeekRepository` HttpClient; remove `REPO_API_KEY` from Railway.
4. Remove `REPO_API_KEY` from `.env.example` and integration defaults once JWT path is green in CI.

---

## External authorization server (GeekOAuth)

Lives **outside** this repository (`GeekOAuth` repo). Responsibilities:

- OIDC/OAuth 2.1 for Geek apps (PKCE public clients, M2M `geekapi`, etc.)
- User credentials in **geek_oauth** database (`asp_net_users`) — **not** platform `public.users`

GeekBackend **does not** store or expose login APIs after M4–M6/O2.

---

## Data boundaries

### Content (GeekAPI + GeekRepository)

- Departments, case studies, use cases — public read via GeekAPI; writes via authenticated platform routes as products require

### Geek SEO data (in GeekRepository)

- Schema `geek_seo`, EF migrations, `repo/seo/*` controllers — persistence only
- GeekSeoBackend calls GeekAPI `api/seo/internal/*` (not GeekRepository directly)

### Out of scope for GeekBackend

- Geek SEO product logic (providers, workers, `/api/seo` routes) — **Geek-SEO / GeekSeoBackend**
- Per-product business tables (restaurant, CRM, etc.) — respective product backends

---

## Deployment (Railway)

| Service | Project | Public host |
|---------|---------|-------------|
| GeekAPI | GeekAPI | `api.geekatyourspot.com` |
| GeekRepository | GeekRepository | `geekrepository-production.up.railway.app` (or custom domain) |

### Environment variables

**GeekAPI**

| Variable | Purpose |
|----------|---------|
| `REPO_URL` | GeekRepository base URL |
| `GEEK_BACKEND_API_KEY` | Inbound trust for auth-storage and admin adapters |
| `CORS_ORIGINS` | Browser origins allowed to call API |
| `PORT` | Listen port |

**GeekRepository**

| Variable | Purpose |
|----------|---------|
| `DATABASE_URL` | PostgreSQL (Supabase/Railway) |

**Target additions (replace repo API key)**

| Variable | Service | Purpose |
|----------|---------|---------|
| `AUTH_SERVER_URL` | GeekRepository (and GeekAPI token client) | Issuer URL for JWT validation / token acquisition (`https://auth.geekatyourspot.com`) |
| `GEEKAPI_CLIENT_ID` / secret | GeekAPI | Machine client for `client_credentials` (names illustrative; use platform secret store) |

**Remove from Railway when JWT S2S is live**

- `REPO_API_KEY`, `OPENIDDICT_*`, `GEEK_*_CLIENT_SECRET` (issuer-era), `AUTH_SERVER_URL` pointing at **api** as issuer, `GEEK_SEO_*`

---

## Security principles

1. **Short-lived, audience-bound tokens** for machine access — not shared passwords in headers.
2. **Least privilege** — `internal.api` only on repo; product scopes only on user tokens from auth.
3. **Single data path** — GeekAPI → GeekRepository; products do not hold DB URLs for platform data.
4. **Secrets in platform vault** — Railway variables or external secret manager; never commit; rotate on leak.
5. **`GEEK_BACKEND_API_KEY`** is for **trusted backends calling GeekAPI** (e.g. auth adapter). It is separate from repo S2S and must not be confused with user sessions.

---

## Database cleanup (OAuth / OpenIddict era)

On startup, `SqlMigrationRunner` applies `Migrations/Sql/0004_drop_oauth_openiddict_and_adapter_storage.sql` (issuer-era tables only; **not** `geek_seo`). Geek SEO tables are created by `ApplySeoMigrationsAsync`. **Greenfield:** the external authorization server owns client/token persistence; this database holds users, devices, platform content, and Geek SEO rows.

---

## Testing

```bash
dotnet build GEEKBACKEND.slnx
dotnet test GEEKBACKEND.slnx
```

Integration tests may use `REPO_API_KEY` until the JWT client-credentials path is wired in test fixtures. Prefer issuing test JWTs from a test issuer over static repo keys when adding new tests.

---

## Related docs

- [README.md](./README.md) — build, run, status table
- [GeekAPI/README.md](./GeekAPI/README.md) — GeekAPI routes and env (update when JWT S2S lands)
