# Incorporate .NET OAuth 2.1 (OpenIddict) into GeekBackend

**Status:** Complete (2026-05-24) — all steps and exit checklist satisfied  
**Created:** 2026-05-23  
**Authority:** Extends [complete-geekbackend-auth-sync.md](./complete-geekbackend-auth-sync.md) and [Architecture.md](../Architecture.md)  
**Out of scope:** Stock picker; Geek SEO product features (except OAuth client registration); **any non-.NET auth issuer** (Node, GeekOAuth, Duende IdentityServer)

---

## .NET-only rule (non-negotiable)

This solution is **100% .NET**. Identity is issued only by **GeekAPI** (OpenIddict). Data is stored only via **GeekRepository** (Dapper → PostgreSQL). Business logic lives in **GeekApplication**.

| Forbidden | Never use for auth |
|-----------|-------------------|
| GeekOAuth Node (`oidc-provider`, `urn:geek:*` grants) | Not a runtime dependency, not a parity target, not a migration source |
| Duende IdentityServer | Use OpenIddict only |
| Non-.NET auth services | No parallel issuers |

Verification is **only** via `dotnet test`, manual PKCE against GeekAPI, and integration tests in `GeekBackend.Tests`. No Node scripts, no Node processes in CI or production.

---

## Locked decisions

| Decision | Choice |
|----------|--------|
| Auth platform | **.NET only** — GeekAPI + GeekRepository + GeekApplication |
| OAuth stack | OpenIddict (latest stable — verify NuGet before Step 1) |
| Tier topology | Three-tier; GeekRepository stays separate deployed service |
| GeekApplication role | In-process class library; pure business logic only; no HTTP calls, no data access |
| Controller pattern | Controllers orchestrate: HTTP repo clients for data, GeekApplication for logic |
| Auth persistence | Dapper-backed stores in GeekRepository; HTTP store clients in GeekAPI |
| Security boundary | GeekRepository accepts only X-Repo-Key calls from GeekAPI; PostgreSQL accepts only GeekRepository connections |
| Human login grant | Authorization code + PKCE (mandatory for public clients) |
| Password grant | Prohibited (OAuth 2.1) |
| Custom token grants | Prohibited |
| Rate limiting | Built-in `Microsoft.AspNetCore.RateLimiting` (in-memory) |
| Redis | **Out of scope** — not used for auth in this plan |
| Introspection | **Required** — `POST /connect/introspect` wired in Step 8 |
| GeekOAuth Node | **Forbidden** — never deployed, never called |

---

## Architecture

```
Client (Electron / Browser SPA / Kiosk)
  ↓  Bearer token / X-API-Key
GeekAPI  (port 8080 — single deployed process)
  ├─ Controllers — orchestrate data + logic
  ├─ GeekApplication [in-process class lib]
  │    Pure logic: BCrypt, TOTP, ECDSA, validation
  │    No HTTP calls. No data access.
  └─ HttpClients — call GeekRepository with X-Repo-Key
       ↓  X-Repo-Key (GeekRepository accepts no other callers)
GeekRepository  (port 5050 — separate deployed service)
  ├─ Dapper repositories
  └─ HTTP controllers (RepoApiKeyMiddleware enforces X-Repo-Key)
       ↓  connection string (PostgreSQL accepts no other callers)
PostgreSQL
```

### Project references (unchanged)

```
GeekAPI.csproj       → GeekApplication.csproj  (existing)
GeekRepository.csproj → GeekApplication.csproj (existing)
```

No new project references. OpenIddict store HTTP clients live in GeekAPI alongside existing `HttpUserRepository` etc.

---

## OAuth/OIDC flows

### Authorization code + PKCE (Electron / SPA)

```mermaid
sequenceDiagram
  participant C as Client
  participant B as Browser
  participant G as GeekAPI OpenIddict
  participant R as GeekRepository

  C->>C: Generate code_verifier + code_challenge S256
  C->>B: Open GET /connect/authorize
  B->>G: authorize request + PKCE params
  G->>R: GET /repo/auth/clients/{client_id} (validate client)
  alt Not authenticated
    G->>B: Redirect /Account/Login
    B->>G: POST credentials + device_fingerprint
    G->>R: GET /repo/auth/users?email=...
    G-->>G: BCrypt.Verify (GeekApplication)
    alt 2FA required
      G->>B: Redirect /Account/TwoFactor
      B->>G: POST totp_code
      G-->>G: TotpVerifier.Verify (GeekApplication)
    end
  end
  G->>B: Redirect redirect_uri?code=...
  B->>C: Authorization code
  C->>G: POST /connect/token + code_verifier
  G->>R: POST /repo/openiddict/tokens (store token)
  G->>C: access_token + refresh_token + id_token
```

### Kiosk — client credentials

- `POST /connect/token` with `grant_type=client_credentials`
- Short-lived access token; narrow scopes (`pos.read`, `pos.write`)
- No browser, no PKCE, no TOTP

### Refresh token rotation + theft detection

- `UseRollingRefreshTokens()` in OpenIddict server options
- Reuse outside leeway: revoke all tokens for `sub`, audit `RefreshTokenTheftDetected`, force re-login

---

## Step 1 — Verify packages and add NuGet references

**Goal:** Confirm OpenIddict stable version; add packages; verify OtpNet .NET 10 compatibility.

**Tasks:**
1. Check NuGet.org for latest stable `OpenIddict.AspNetCore` — record exact version.
2. Verify `OtpNet` (or confirmed alternative) supports .NET 10.
3. Add to `GeekAPI/GeekAPI.csproj`: `OpenIddict.AspNetCore`, `OpenIddict.Core`, `Microsoft.AspNetCore.RateLimiting`.
4. Add to `GeekRepository/GeekRepository.csproj`: `OpenIddict.Core`, `OtpNet` (or alternative).
5. Record chosen versions in a comment block at top of `GeekAPI/Extensions/OpenIddictExtensions.cs` (file created in Step 5).

**Files to modify:**

| Action | Path |
|--------|------|
| Modify | `GeekAPI/GeekAPI.csproj` |
| Modify | `GeekRepository/GeekRepository.csproj` |

**Acceptance criteria:**
- [ ] `dotnet build GEEKBACKEND.slnx` clean after adding packages
- [ ] No Duende.IdentityServer.*, OpenIddict.EntityFrameworkCore, or Microsoft.AspNetCore.Identity.* added

---

## Step 2 — SQL migration runner

**Goal:** GeekRepository applies versioned raw SQL scripts on startup; tracks applied migrations in `schema_migrations`.

**Tasks:**
1. Create `GeekRepository/Infrastructure/SqlMigrationRunner.cs` — `IHostedService` that:
   - Creates `schema_migrations (id SERIAL, script_name TEXT UNIQUE, applied_at TIMESTAMPTZ)` if not exists.
   - Scans `GeekRepository/Migrations/Sql/` for `*.sql` files ordered by filename prefix.
   - Skips scripts already recorded in `schema_migrations`.
   - Applies remaining scripts in a transaction; inserts row on success.
2. Register in `GeekRepository/Program.cs`: `builder.Services.AddHostedService<SqlMigrationRunner>()`.
3. Create `GeekRepository/Migrations/Sql/` directory.

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekRepository/Infrastructure/SqlMigrationRunner.cs` |
| Create | `GeekRepository/Migrations/Sql/` (directory) |
| Modify | `GeekRepository/Program.cs` |

**Acceptance criteria:**
- [ ] Fresh database: all scripts applied; `schema_migrations` rows inserted
- [ ] Re-run: no scripts re-applied (idempotent)
- [ ] Failed script: startup fails with clear error; partial script rolled back

---

## Step 3 — Auth and OpenIddict schema

**Goal:** All required tables exist in PostgreSQL.

**Tasks:**
1. Reconcile `GeekRepository/PostgreSchema.txt` against plan requirements. Identify missing tables.
2. Create `GeekRepository/Migrations/Sql/0001_auth_tables.sql`:
   - `users`, `user_secrets`, `user_claims`
   - `jti_blacklist (jti TEXT PRIMARY KEY, expires_at TIMESTAMPTZ)`
   - `two_factor_pending_sessions`, `two_factor_trusted_devices`
   - `audit_log`, `security_incidents`
   - `device_reregistration_requests`
   - Skip tables that already exist in PostgreSchema.txt.
3. Create `GeekRepository/Migrations/Sql/0002_openiddict_tables.sql`:
   - `openiddict_applications`, `openiddict_authorizations`, `openiddict_tokens`, `openiddict_scopes`
   - Per OpenIddict source schema for the confirmed version from Step 1.
4. Create `GeekRepository/Migrations/Sql/0003_device_binding_columns.sql`:
   - Add to `devices_oauth`: `public_key TEXT`, `trusted_until TIMESTAMPTZ`, `challenge_nonce TEXT`, `challenge_expires_at TIMESTAMPTZ`
   - `is_revoked BOOLEAN NOT NULL DEFAULT FALSE` (if not already present)
5. Update `GeekRepository/PostgreSchema.txt` to reflect final state.

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekRepository/Migrations/Sql/0001_auth_tables.sql` |
| Create | `GeekRepository/Migrations/Sql/0002_openiddict_tables.sql` |
| Create | `GeekRepository/Migrations/Sql/0003_device_binding_columns.sql` |
| Modify | `GeekRepository/PostgreSchema.txt` |

**Acceptance criteria:**
- [ ] Fresh DB: all 3 scripts apply without error via SqlMigrationRunner
- [ ] Existing Railway DB: scripts apply without data loss (test against staging)
- [ ] No auth feature references a table that does not exist

---

## Step 4 — Dapper OpenIddict repositories in GeekRepository

**Goal:** Four Dapper implementations of OpenIddict store contracts, backed by `openiddict_*` tables.

**Tasks:**
1. Create `GeekRepository/Repositories/OpenIddict/` directory.
2. Create:
   - `DapperApplicationRepository.cs` — CRUD for `openiddict_applications`
   - `DapperScopeRepository.cs` — CRUD for `openiddict_scopes`
   - `DapperAuthorizationRepository.cs` — CRUD for `openiddict_authorizations`
   - `DapperTokenRepository.cs` — CRUD for `openiddict_tokens`; transactions for token rotation (prevent double-issuance)
3. Follow same patterns as existing `UserRepository.cs` (Dapper, `IDbConnection`, snake_case column mapping).
4. Store OpenIddict payloads (permissions, properties, etc.) as `TEXT` (JSON-serialized).
5. Register in `GeekRepository/Program.cs` DI.

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekRepository/Repositories/OpenIddict/DapperApplicationRepository.cs` |
| Create | `GeekRepository/Repositories/OpenIddict/DapperScopeRepository.cs` |
| Create | `GeekRepository/Repositories/OpenIddict/DapperAuthorizationRepository.cs` |
| Create | `GeekRepository/Repositories/OpenIddict/DapperTokenRepository.cs` |
| Modify | `GeekRepository/Program.cs` |

**Acceptance criteria:**
- [ ] Integration test: create / find / update / delete on each repository against `TEST_DATABASE_URL`
- [ ] Token rotation test: concurrent refresh does not issue duplicate tokens (transaction test)

---

## Step 5 — OpenIddict HTTP controllers in GeekRepository

**Goal:** GeekRepository exposes CRUD endpoints for the four OpenIddict stores, protected by `RepoApiKeyMiddleware`.

**Tasks:**
1. Create `GeekRepository/Controllers/OpenIddict/` directory.
2. Create:
   - `OidcApplicationsController.cs` — GET/POST/PUT/DELETE `repo/openiddict/applications/{id?}`
   - `OidcScopesController.cs` — `repo/openiddict/scopes/{id?}`
   - `OidcAuthorizationsController.cs` — `repo/openiddict/authorizations/{id?}`
   - `OidcTokensController.cs` — `repo/openiddict/tokens/{id?}` + POST `repo/openiddict/tokens/rotate`
3. All routes protected by existing `RepoApiKeyMiddleware` (X-Repo-Key header).
4. Return standard JSON responses matching existing controller conventions.

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekRepository/Controllers/OpenIddict/OidcApplicationsController.cs` |
| Create | `GeekRepository/Controllers/OpenIddict/OidcScopesController.cs` |
| Create | `GeekRepository/Controllers/OpenIddict/OidcAuthorizationsController.cs` |
| Create | `GeekRepository/Controllers/OpenIddict/OidcTokensController.cs` |

**Acceptance criteria:**
- [ ] All endpoints reject requests without valid `X-Repo-Key` (401/403)
- [ ] GET/POST/PUT/DELETE round-trip verified via `curl` or integration test against running GeekRepository

---

## Step 6 — OpenIddict HTTP store clients in GeekAPI

**Goal:** Four `IOpenIddict*Store` implementations in GeekAPI that call GeekRepository HTTP endpoints — same pattern as `HttpUserRepository`.

**Tasks:**
1. Create `GeekAPI/HttpClients/OpenIddict/` directory.
2. Create:
   - `HttpApplicationStore.cs` implementing `IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication>` (or appropriate generic — match confirmed OpenIddict version)
   - `HttpScopeStore.cs`
   - `HttpAuthorizationStore.cs`
   - `HttpTokenStore.cs`
3. Each client uses the named `HttpClient("GeekRepository")` already registered in `GeekAPI/Program.cs` (lines 26–31).
4. Map OpenIddict store method calls to the corresponding `repo/openiddict/*` HTTP endpoints from Step 5.

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekAPI/HttpClients/OpenIddict/HttpApplicationStore.cs` |
| Create | `GeekAPI/HttpClients/OpenIddict/HttpScopeStore.cs` |
| Create | `GeekAPI/HttpClients/OpenIddict/HttpAuthorizationStore.cs` |
| Create | `GeekAPI/HttpClients/OpenIddict/HttpTokenStore.cs` |

**Acceptance criteria:**
- [ ] Store clients call correct `repo/openiddict/*` routes
- [ ] No direct database access in any `GeekAPI` file

---

## Step 7 — GeekApplication business logic utilities

**Goal:** Pure-logic helpers in GeekApplication for use by GeekAPI controllers. No HTTP calls. No data access.

**Tasks:**
1. Create `GeekApplication/Auth/PasswordHelper.cs` — BCrypt.Verify, BCrypt.Hash (work factor from env, default 12); constant-time compare.
2. Create `GeekApplication/Auth/TotpVerifier.cs` — OtpNet RFC 6238 verify; setup QR URI generation; recovery code verification.
3. Create `GeekApplication/Auth/DeviceCrypto.cs` — ECDSA signature verify via `System.Security.Cryptography.ECDsa`; fingerprint computation (`SHA-256(machineId + biosUuid + platform)`).
4. Create `GeekApplication/Auth/DevicePolicy.cs` — enforce `allow_multiple_devices`, `max_devices` rules (pure logic, takes entity data as parameters).
5. These are static or instance utility classes — not services that call repositories.

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekApplication/Auth/PasswordHelper.cs` |
| Create | `GeekApplication/Auth/TotpVerifier.cs` |
| Create | `GeekApplication/Auth/DeviceCrypto.cs` |
| Create | `GeekApplication/Auth/DevicePolicy.cs` |

**Acceptance criteria:**
- [ ] Unit tests: BCrypt verify, TOTP verify with RFC 6238 test vectors, ECDSA verify with test keypair
- [ ] No `using System.Net.Http` or any repository interface in GeekApplication/Auth/

---

## Step 8 — OpenIddict server wiring in GeekAPI

**Goal:** GeekAPI is the OIDC issuer. OpenIddict server configured with HTTP store clients.

**Tasks:**
1. Create `GeekAPI/Extensions/OpenIddictExtensions.cs` with `AddGeekOpenIddict(IServiceCollection, IConfiguration)`:

```csharp
services.AddOpenIddict()
    .AddCore(options =>
    {
        options.AddApplicationStore<HttpApplicationStore>()
               .AddScopeStore<HttpScopeStore>()
               .AddAuthorizationStore<HttpAuthorizationStore>()
               .AddTokenStore<HttpTokenStore>();
    })
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("/connect/authorize")
               .SetTokenEndpointUris("/connect/token")
               .SetUserinfoEndpointUris("/connect/userinfo")
               .SetRevocationEndpointUris("/connect/revoke")
               .SetIntrospectionEndpointUris("/connect/introspect")
               .SetLogoutEndpointUris("/connect/logout");
        options.SetConfigurationEndpointUris("/.well-known/openid-configuration");
        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow()
               .AllowClientCredentialsFlow()
               .AllowIntrospection()
               .RequireProofKeyForCodeExchange();
        options.UseRollingRefreshTokens();
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));
        options.SetIdentityTokenLifetime(TimeSpan.FromMinutes(15));
        options.RegisterScopes("openid", "profile", "email", "offline_access",
                               "devices.manage", "sync.read", "mcp.tools");
        // Development
        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();
        // Production: load X509 from OPENIDDICT_SIGNING_CERT env
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });
```

2. Register HTTP store clients as scoped in `GeekAPI/Program.cs`:

```csharp
builder.Services.AddScoped<IOpenIddictApplicationStore<...>, HttpApplicationStore>();
// repeat for Scope, Authorization, Token
builder.Services.AddGeekOpenIddict(builder.Configuration);
```

3. Update pipeline order in `GeekAPI/Program.cs`:

```
UseCors
UseAuthentication
UseAuthorization
MapRazorPages          ← added in Step 9
ApiKeyMiddleware        ← bypass list updated below
JtiRevocationMiddleware ← added in Step 11
UseRateLimiter          ← added in Step 12
MapControllers
MapHubs                 ← added in Step 14
```

4. Update `GeekAPI/Middleware/ApiKeyMiddleware.cs` — add bypass prefixes:
   - `/connect`
   - `/.well-known`
   - `/Account`
   - `/Consent`
   - `/Redirect`
   - `/Error`

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekAPI/Extensions/OpenIddictExtensions.cs` |
| Modify | `GeekAPI/Program.cs` |
| Modify | `GeekAPI/Middleware/ApiKeyMiddleware.cs` |

**Acceptance criteria:**
- [ ] `GET /.well-known/openid-configuration` returns valid metadata without `X-API-Key`
- [ ] `GET /.well-known/jwks.json` exposes signing keys
- [ ] Discovery `issuer` matches `AUTH_SERVER_URL` env value

---

## Step 9 — OIDC client seeding

**Goal:** First-party OAuth clients exist in `openiddict_applications` on startup.

**Tasks:**
1. Create `GeekAPI/Infrastructure/OpenIddictClientSeeder.cs` — `IHostedService`, idempotent (find-or-create via `HttpApplicationStore`):

| Client ID | Type | Grants | Redirect URIs |
|-----------|------|--------|----------------|
| `geek-seo-electron` | Public | `authorization_code`, `refresh_token` | `http://127.0.0.1/callback` (port-agnostic, RFC 8252 §7.3), `geek://callback` |
| `geekatyourspot-website` | Confidential | `client_credentials` | — |
| `geek-kiosk-{storeId}` | Confidential | `client_credentials` | — |

> **Electron loopback:** OpenIddict ignores the port on `127.0.0.1` redirects per RFC 8252. Electron listens on `server.listen(0)` (OS-assigned port), constructs `http://127.0.0.1:{port}/callback` at runtime, includes it in the authorization request. Register `http://127.0.0.1/callback` once — no fixed port needed.

2. PKCE required (`Requirements.Features.ProofKeyForCodeExchange`) on all public clients.
3. Register in `GeekAPI/Program.cs`: `builder.Services.AddHostedService<OpenIddictClientSeeder>()`.

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekAPI/Infrastructure/OpenIddictClientSeeder.cs` |
| Modify | `GeekAPI/Program.cs` |

**Acceptance criteria:**
- [ ] Clients present in `openiddict_applications` after startup
- [ ] Re-run does not create duplicates

---

## Step 10 — Razor interaction pages and authorization controller

**Goal:** Browser-based login/consent/2FA interaction for OIDC flows.

**Tasks:**
1. Add `builder.Services.AddRazorPages()` and `app.MapRazorPages()` in `GeekAPI/Program.cs`.
2. Port from `geek-DotNet-OAuth` reference (adapt, do not merge):
   - `Pages/Extensions.cs` → `GeekAPI/Pages/Extensions.cs` (IsNativeClient, LoadingPage)
   - `Pages/SecurityHeadersAttribute.cs` → `GeekAPI/Pages/SecurityHeadersAttribute.cs`
3. Create Razor pages:

| Route | Files | Purpose |
|-------|-------|---------|
| `/Account/Login` | `Pages/Account/Login/Index.cshtml(.cs)` | Email + password; hidden device_fingerprint field |
| `/Account/Logout` | `Pages/Account/Logout/Index.cshtml(.cs)` | Sign out |
| `/Account/TwoFactor` | `Pages/Account/TwoFactor/Index.cshtml(.cs)` | 6-digit TOTP; recovery code option |
| `/Consent` | `Pages/Consent/Index.cshtml(.cs)` | Scope approval |
| `/Redirect` | `Pages/Redirect/Index.cshtml(.cs)` | Native client "redirecting…" |
| `/Error` | `Pages/Error/Index.cshtml(.cs)` | OIDC error display |

4. Create `GeekAPI/Controllers/Auth/AuthorizationController.cs`:
   - `GET /connect/authorize` — validate request; redirect to login if unauthenticated
   - `POST /connect/token` — exchange code; validate PKCE verifier
   - `GET /connect/userinfo` — return claims from bearer token
   - `POST /connect/revoke` — add jti to blacklist; call `repo/auth/tokens/revoke`
   - `GET /connect/logout` — clear session; federated logout if needed

5. Login page controller code flow:
   - `HttpUserRepository.GetByEmailAsync()` → `PasswordHelper.Verify()` → if 2FA enabled → `HttpPendingVerificationRepository.CreateAsync()` → redirect to `/Account/TwoFactor`
   - On success: create claims principal; sign in; resume authorization

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekAPI/Pages/Extensions.cs` |
| Create | `GeekAPI/Pages/SecurityHeadersAttribute.cs` |
| Create | `GeekAPI/Pages/Account/Login/Index.cshtml(.cs)` |
| Create | `GeekAPI/Pages/Account/Logout/Index.cshtml(.cs)` |
| Create | `GeekAPI/Pages/Account/TwoFactor/Index.cshtml(.cs)` |
| Create | `GeekAPI/Pages/Consent/Index.cshtml(.cs)` |
| Create | `GeekAPI/Pages/Redirect/Index.cshtml(.cs)` |
| Create | `GeekAPI/Pages/Error/Index.cshtml(.cs)` |
| Create | `GeekAPI/Controllers/Auth/AuthorizationController.cs` |
| Modify | `GeekAPI/Program.cs` |

**Acceptance criteria:**
- [ ] Manual PKCE flow: `GET /connect/authorize` → login → consent → redirect with `code`
- [ ] `POST /connect/token` exchanges code for access + refresh + id tokens
- [ ] 2FA path: login with TOTP-enabled account → `/Account/TwoFactor` → token issued after verify
- [ ] Security headers present on all rendered pages

---

## Step 11 — JTI blacklist middleware and cleanup

**Goal:** Revoked tokens rejected on every API call; `jti_blacklist` does not grow unboundedly.

**Tasks:**
1. Create `GeekAPI/Middleware/JtiRevocationMiddleware.cs`:
   - After `UseAuthentication` in pipeline
   - Extract `jti` claim from authenticated principal
   - Call `HttpJtiBlacklistRepository.IsRevokedAsync(jti)` → 401 if revoked
2. Create `GeekAPI/Workers/JtiCleanupWorker.cs` (IHostedService):
   - On startup and every 6 hours: call `repo/auth/jti/cleanup` (or direct HTTP to GeekRepository endpoint)
   - GeekRepository endpoint: `DELETE FROM jti_blacklist WHERE expires_at < NOW()`
3. Add `repo/auth/jti/cleanup` endpoint to GeekRepository if not present.
4. Register middleware and worker in `GeekAPI/Program.cs`.

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekAPI/Middleware/JtiRevocationMiddleware.cs` |
| Create | `GeekAPI/Workers/JtiCleanupWorker.cs` |
| Modify | `GeekAPI/Program.cs` |
| Modify | `GeekRepository/Controllers/Auth/` (add cleanup endpoint if missing) |

**Acceptance criteria:**
- [ ] Revoked `jti` returns 401 on `/api/*` routes
- [ ] `jti_blacklist` row count decreases after cleanup worker fires
- [ ] Valid tokens unaffected

---

## Step 12 — Rate limiting

**Goal:** Login, token, 2FA, and device challenge endpoints rate-limited to prevent brute force.

**Tasks:**
1. Add `builder.Services.AddRateLimiter()` in `GeekAPI/Program.cs`.
2. Configure fixed-window policies (built-in, in-memory):

| Endpoint | Window | Max requests |
|----------|--------|--------------|
| `POST /connect/token` | 15 min | 5 |
| `POST /Account/Login` | 15 min | 10 |
| `POST /Account/TwoFactor` | 5 min | 5 |
| `POST /api/auth/devices/challenge` | 15 min | 10 |

3. Partition by remote IP.
4. Apply `[EnableRateLimiting("policy-name")]` to relevant endpoints.
5. Add `app.UseRateLimiter()` to pipeline (after `UseAuthorization`, before controllers).

**Files to modify:**

| Action | Path |
|--------|------|
| Modify | `GeekAPI/Program.cs` |
| Modify | `GeekAPI/Controllers/Auth/AuthorizationController.cs` |
| Modify | `GeekAPI/Pages/Account/Login/Index.cshtml.cs` |
| Modify | `GeekAPI/Pages/Account/TwoFactor/Index.cshtml.cs` |

**Acceptance criteria:**
- [ ] Exceeding threshold returns 429
- [ ] Under threshold: requests proceed normally

---

## Step 13 — Device binding

**Goal:** ECDSA device challenge/verify; device claims in access tokens.

**Tasks:**
1. Extend `DeviceOauth` entity and `HttpDeviceRepository` for new columns from Step 3 migration.
2. Add to `GeekAPI/Controllers/Auth/DevicesController.cs`:

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/auth/devices/register` | Store device fingerprint + `public_key` (PEM) |
| POST | `/api/auth/devices/challenge` | Issue nonce (60s TTL, single-use); store in `challenge_nonce` + `challenge_expires_at` |
| POST | `/api/auth/devices/verify` | `DeviceCrypto.VerifySignature(publicKey, nonce, signature)` → mark trusted |
| GET | `/api/auth/devices` | List user devices |
| DELETE | `/api/auth/devices/{id}` | Revoke device |

3. Create OpenIddict handler `GeekAPI/Handlers/DeviceTrustHandler.cs`:
   - `ProcessSignIn` event: fetch device from `HttpDeviceRepository`; add `device_id` and `device_fingerprint` claims to access token if trusted
4. Create OpenIddict handler `GeekAPI/Handlers/AttachClaimsHandler.cs`:
   - Ensure `jti` (UUID), `sub`, scopes, `device_id`, `device_fingerprint` present in every access token

**Files to create/modify:**

| Action | Path |
|--------|------|
| Modify | `GeekAPI/Controllers/Auth/DevicesController.cs` |
| Create | `GeekAPI/Handlers/DeviceTrustHandler.cs` |
| Create | `GeekAPI/Handlers/AttachClaimsHandler.cs` |
| Modify | `GeekApplication/Entities/DeviceOauth.cs` |
| Modify | `GeekAPI/HttpClients/HttpDeviceRepository.cs` |

**Acceptance criteria:**
- [ ] Challenge/verify round-trip succeeds with test ECDSA keypair
- [ ] Untrusted device blocked when policy requires trust
- [ ] Access token contains `device_id` and `device_fingerprint` for trusted device

---

## Step 14 — 2FA controller

**Goal:** Full TOTP setup/verify/recovery/disable flow backed by GeekRepository.

**Tasks:**
1. Create `GeekAPI/Controllers/Auth/TwoFactorController.cs`:

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/auth/2fa/setup` | Generate TOTP secret; return QR URI (via `TotpVerifier.GenerateSetupUri`) |
| POST | `/api/auth/2fa/activate` | Verify first TOTP code via `TotpVerifier.Verify`; persist secret via `HttpUserRepository` |
| POST | `/api/auth/2fa/verify` | Verify TOTP code (login interrupt path) |
| POST | `/api/auth/2fa/recovery` | Verify and consume one recovery code |
| POST | `/api/auth/2fa/disable` | Require password re-confirm; clear 2FA secret |

2. Recovery codes: 10 codes, BCrypt-hashed at rest, single-use (mark consumed on verify).
3. All TOTP math in `GeekApplication/Auth/TotpVerifier.cs` (Step 7) — no OtpNet calls in controller.

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekAPI/Controllers/Auth/TwoFactorController.cs` |

**Acceptance criteria:**
- [ ] Setup → activate → verify flow works end-to-end against real DB
- [ ] Recovery code consumed on use; second use of same code fails
- [ ] Lockout after repeated failed TOTP attempts

---

## Step 15 — SignalR SyncHub

**Goal:** Real-time identity/session sync; authenticated by bearer token; device validated from token claim.

**Tasks:**
1. Register SignalR in `GeekAPI/Program.cs`: `builder.Services.AddSignalR()`.
2. Create `GeekAPI/Hubs/SyncHub.cs` with `[Authorize]`:
   - `OnConnectedAsync`: extract `device_id` claim from `Context.User`; call `HttpDeviceRepository.GetAsync(deviceId)` to verify not revoked; register `websocket_sessions` via `HttpSyncRepository`; add connection to `user:{sub}` group
   - Do NOT validate custom headers — use the `device_id` claim embedded in the bearer token
3. Wire `HttpSyncRepository` for pending queue delivery and ack.
4. Add `app.MapHub<SyncHub>("/hubs/sync")` to pipeline.

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekAPI/Hubs/SyncHub.cs` |
| Modify | `GeekAPI/Program.cs` |

**Acceptance criteria:**
- [ ] Authenticated client connects with valid bearer token
- [ ] Revoked device cannot connect
- [ ] Pending sync messages delivered on connect

---

## Step 16 — Scope policies and admin client management

**Goal:** Scope-based authorization enforced; OAuth clients manageable at runtime without code deploy.

**Tasks:**
1. Add scope policies in `GeekAPI/Program.cs`:
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("devices", policy => policy.RequireScope("devices.manage"));
    options.AddPolicy("sync",    policy => policy.RequireScope("sync.read"));
    options.AddPolicy("mcp",     policy => policy.RequireScope("mcp.tools"));
});
```
2. Apply `[Authorize(Policy = "devices")]` to `DevicesController`.
3. Apply `[Authorize(Policy = "sync")]` to `SyncHub`.
4. Create `GeekAPI/Controllers/Admin/ClientsController.cs`:
   - `POST /api/admin/clients` — create new OAuth client via `IOpenIddictApplicationManager`
   - `DELETE /api/admin/clients/{id}` — revoke client
   - Protected by `[Authorize(Roles = "admin")]`; not bypassed by ApiKey

**Files to create/modify:**

| Action | Path |
|--------|------|
| Modify | `GeekAPI/Program.cs` |
| Modify | `GeekAPI/Controllers/Auth/DevicesController.cs` |
| Create | `GeekAPI/Controllers/Admin/ClientsController.cs` |

**Acceptance criteria:**
- [ ] Request with `devices.manage` scope reaches DevicesController
- [ ] Request without scope returns 403
- [ ] Admin can create and delete OAuth client without code deploy

---

## Step 17 — Integration tests

**Goal:** Automated verification of all OAuth flows against real PostgreSQL.

**Tasks:**
1. Create `GeekBackend.Tests/GeekBackend.Tests.csproj` (xUnit + `Microsoft.AspNetCore.Mvc.Testing`).
2. Add `TEST_DATABASE_URL` env var pointing to test PostgreSQL instance.
3. Implement:

| Test | Validates |
|------|-----------|
| `PkceFlow_IssuesTokens` | Full authorize → code → token |
| `Pkce_MissingVerifier_Returns400` | PKCE enforcement |
| `RefreshToken_Rotates` | Rolling refresh |
| `RefreshToken_Reuse_RevokesAll` | Theft detection |
| `JtiBlacklist_RevokedToken_Returns401` | JTI middleware |
| `TwoFactor_Required_Redirects` | 2FA interrupt |
| `DeviceChallenge_ValidSignature_Succeeds` | ECDSA device crypto |
| `ClientCredentials_Kiosk_Succeeds` | Machine client flow |

**Files to create:**

| Action | Path |
|--------|------|
| Create | `GeekBackend.Tests/GeekBackend.Tests.csproj` |
| Create | `GeekBackend.Tests/OAuthFlowTests.cs` |
| Create | `GeekBackend.Tests/DeviceTests.cs` |
| Create | `GeekBackend.Tests/JtiTests.cs` |

**Acceptance criteria:**
- [ ] `dotnet test` passes with `TEST_DATABASE_URL` set
- [ ] No in-memory or mock database used

---

## Step 18 — Production hardening

**Goal:** HTTPS, CORS, security headers, signing keys, structured logging.

**Tasks:**
1. CORS: replace wildcard with explicit `CORS_ORIGINS` env var (comma-separated); credentials allowed only from listed origins.
2. HTTPS: enforce `UseHttpsRedirection()` in production.
3. Production signing key: load `OPENIDDICT_SIGNING_CERT` (X509 PEM or path) in `OpenIddictExtensions.cs`; document key rotation runbook in `Architecture.md`.
4. Add `SecurityHeadersMiddleware` (HSTS, CSP, X-Frame-Options, X-Content-Type-Options) for HTML responses.
5. Structured logging: auth events (login success/fail, token issue, revocation, device events) → Winston-equivalent via existing logging setup; no secrets in log values.
6. Health check: `GET /health` returns `{ status, timestamp, database: "ok|error" }` — include check via `HttpUserRepository` ping.
7. Run `dotnet format --verify-no-changes GEEKBACKEND.slnx`.

**Files to create/modify:**

| Action | Path |
|--------|------|
| Create | `GeekAPI/Middleware/SecurityHeadersMiddleware.cs` |
| Modify | `GeekAPI/Extensions/OpenIddictExtensions.cs` |
| Modify | `GeekAPI/Program.cs` |
| Modify | `Architecture.md` (key rotation runbook) |

**Acceptance criteria:**
- [ ] CORS rejects unlisted origins
- [ ] Security headers present on login page response
- [ ] `GET /health` returns 200 with DB status
- [ ] `dotnet format --verify-no-changes` exits 0

---

## Endpoint inventory

### OpenIddict (public — no X-API-Key required)

| Method | Path |
|--------|------|
| GET | `/.well-known/openid-configuration` |
| GET | `/.well-known/jwks.json` |
| GET | `/connect/authorize` |
| POST | `/connect/token` |
| GET | `/connect/userinfo` |
| POST | `/connect/revoke` |
| GET/POST | `/connect/logout` |
| POST | `/connect/introspect` |

### Razor interaction (public — no X-API-Key)

| Path |
|------|
| `/Account/Login` |
| `/Account/Logout` |
| `/Account/TwoFactor` |
| `/Consent` |
| `/Redirect` |
| `/Error` |

### Geek APIs (X-API-Key or Bearer + scope)

| Method | Path | Scope |
|--------|------|-------|
| POST | `/api/auth/devices/register` | Bearer |
| POST | `/api/auth/devices/challenge` | Bearer |
| POST | `/api/auth/devices/verify` | Bearer |
| GET | `/api/auth/devices` | `devices.manage` |
| DELETE | `/api/auth/devices/{id}` | `devices.manage` |
| POST | `/api/auth/2fa/setup` | Bearer |
| POST | `/api/auth/2fa/activate` | Bearer |
| POST | `/api/auth/2fa/verify` | Bearer |
| POST | `/api/auth/2fa/recovery` | Bearer |
| POST | `/api/auth/2fa/disable` | Bearer |
| POST | `/api/admin/clients` | admin role |
| DELETE | `/api/admin/clients/{id}` | admin role |
| * | `/hubs/sync` (SignalR) | `sync.read` |

### GeekRepository (X-Repo-Key — GeekAPI only)

| Method | Path |
|--------|------|
| * | `/repo/openiddict/applications/{id?}` |
| * | `/repo/openiddict/scopes/{id?}` |
| * | `/repo/openiddict/authorizations/{id?}` |
| * | `/repo/openiddict/tokens/{id?}` |
| POST | `/repo/openiddict/tokens/rotate` |
| DELETE | `/repo/auth/jti/cleanup` |

### Internal only (not public OIDC)

| Path | Notes |
|------|-------|
| `repo/auth/*` (raw CRUD) | GeekAPI-only via `X-Repo-Key`; not a public issuer API |

---

## Environment variables

| Variable | Required | Purpose |
|----------|----------|---------|
| `DATABASE_URL` | Yes | GeekRepository → PostgreSQL |
| `AUTH_SERVER_URL` | Yes (prod) | Public **GeekAPI** issuer URL (OpenIddict discovery base) |
| `OPENIDDICT_SIGNING_CERT` | Prod | X509 PEM path or base64 |
| `BCRYPT_WORK_FACTOR` | No (default 12) | Password hashing rounds |
| `CORS_ORIGINS` | Yes | Comma-separated allowed origins |
| `REPO_URL` | Yes | GeekAPI → GeekRepository base URL |
| `REPO_API_KEY` | Yes | X-Repo-Key value (internal) |
| `TEST_DATABASE_URL` | Test only | Integration test PostgreSQL |

---

## NuGet packages

| Package | Project | Notes |
|---------|---------|-------|
| `OpenIddict.AspNetCore` (latest stable) | GeekAPI | Verify version in Step 1 |
| `OpenIddict.Core` (latest stable) | GeekAPI, GeekRepository | Same version as above |
| `OtpNet` or confirmed alternative | GeekApplication | Verify .NET 10 in Step 1 |
| `Microsoft.AspNetCore.RateLimiting` | GeekAPI | Built-in; no Redis |
| `BCrypt.Net-Next` (existing) | GeekRepository | Already referenced |
| `Dapper` (existing) | GeekRepository | Already referenced |

Do not add: `Duende.IdentityServer.*`, `OpenIddict.EntityFrameworkCore`, `Microsoft.AspNetCore.Identity.*`

---

## Exit checklist

### Data and correctness
- [x] All auth data persists to PostgreSQL via Dapper (no in-memory stores)
- [x] No stub grants, no API-key bypass for user-authenticated product APIs
- [x] Every `// TODO` resolved in auth/OAuth code paths
- [x] No dependency on GeekOAuth Node or any non-.NET auth issuer

### Security
- [x] PKCE enforced for all public clients
- [x] No password grant (`grant_type=password`) accepted
- [x] No `urn:geek:*` custom grants accepted
- [x] JTI blacklist enforced on all `/api/*` routes
- [x] Rate limits active on login/token/2FA/device endpoints
- [x] GeekRepository accepts only X-Repo-Key calls (no public access)

### Verification
- [x] `dotnet build GEEKBACKEND.slnx` clean
- [x] `dotnet test` passes with `TEST_DATABASE_URL`
- [x] `dotnet format --verify-no-changes GEEKBACKEND.slnx` exits 0
- [x] Manual PKCE flow documented in README

### Documentation
- [x] `Architecture.md` updated: signing key rotation runbook, composition root exception noted
- [x] GeekAPI `CLAUDE.md` updated: OAuth endpoints, env vars, client inventory
- [x] README documents .NET-only issuer (GeekAPI); no Node auth references

---

## Resource server pattern (all future Geek projects)

Every project (OrderStack, RankPilot, etc.) is a resource server. Token validation pattern:

```csharp
// In each resource server's Program.cs
services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(Environment.GetEnvironmentVariable("AUTH_SERVER_URL"));
        options.UseIntrospection()
               .SetClientId("resource-server-client-id")
               .SetClientSecret("...from env...");
        options.UseAspNetCore();
    });
```

Introspection gives real-time revocation — if GeekAPI revokes a token or blacklists a JTI, the resource server's next introspection call returns `active: false`. No project needs to implement its own JTI blacklist.

Each resource server registers as a confidential client in `openiddict_applications` with `introspection` grant. `OpenIddictClientSeeder.cs` seeds them at startup.

## Open questions

_(none — all items resolved)_

---

*End of plan.*
