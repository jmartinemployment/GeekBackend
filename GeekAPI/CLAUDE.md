# GeekAPI — OAuth 2.1 / OIDC issuer

GeekAPI is the **only** deployed identity issuer for Geek apps. It hosts OpenIddict 7, Razor login/2FA UI, and product APIs. Persistence is **never** in-process: all auth data goes to GeekRepository over HTTP (`X-Repo-Key`).

## Endpoints

### OpenIddict (public — no `X-API-Key`)

| Method | Path |
|--------|------|
| GET | `/.well-known/openid-configuration` |
| GET | `/.well-known/jwks.json` |
| GET/POST | `/connect/authorize` |
| POST | `/connect/token` |
| GET/POST | `/connect/userinfo` |
| POST | `/connect/revoke` |
| POST | `/connect/introspect` |
| GET/POST | `/connect/logout` |

### Razor UI (public)

| Path |
|------|
| `/Account/Login` |
| `/Account/TwoFactor` |
| `/Account/Logout` |
| `/Consent` |
| `/Redirect` |
| `/Error` |

### Authenticated APIs (Bearer + scope)

| Method | Path | Policy / scope |
|--------|------|----------------|
| * | `/api/auth/devices/*` | `devices.manage` |
| * | `/api/auth/2fa/*` | Bearer (OpenIddict validation) |
| * | `/api/admin/clients` | `admin` role |
| * | `/hubs/sync` | `sync.read` |

### Health

| Method | Path |
|--------|------|
| GET | `/health` |

## Seeded OAuth clients

| Client ID | Type | Use |
|-----------|------|-----|
| `geek-seo-electron` | Public + PKCE | Desktop SEO app |
| `geekatyourspot-website` | Confidential | Website / MCP (`client_credentials`) |
| `geek-resource-server` | Confidential | Resource server introspection |

## Required environment variables

| Variable | Purpose |
|----------|---------|
| `AUTH_SERVER_URL` | Public issuer URL (e.g. `https://api.geekatyourspot.com`) |
| `REPO_URL` | GeekRepository base URL |
| `REPO_API_KEY` | Must match GeekRepository |
| `GEEK_WEBSITE_CLIENT_SECRET` | Seeded confidential client |
| `GEEK_RESOURCE_SERVER_SECRET` | Introspection client |
| `GEEK_BACKEND_API_KEY` | `X-API-Key` for non-OAuth product APIs |
| `CORS_ORIGINS` | Comma-separated browser origins |
| `OPENIDDICT_SIGNING_CERT` | Production X509 (PEM, path, or base64 PFX) |
| `OPENIDDICT_SIGNING_CERT_PASSWORD` | Optional PFX password |

## Key files

| Path | Role |
|------|------|
| `Extensions/OpenIddictExtensions.cs` | OpenIddict server + validation registration |
| `Infrastructure/OpenIddictClientSeeder.cs` | First-party client seeding |
| `Infrastructure/OpenIddictCertificateLoader.cs` | Prod signing/encryption certs |
| `Controllers/Auth/AuthorizationController.cs` | `/connect/*` passthrough |
| `Handlers/TokenSignInHandlers.cs` | JTI + device claims on token issue |
| `Handlers/RefreshTokenTheftHandler.cs` | Revoke-all on refresh reuse |
| `Middleware/JtiRevocationMiddleware.cs` | JTI blacklist on `/api/*` |
| `Middleware/ApiKeyMiddleware.cs` | API key gate (OAuth paths exempt) |
| `HttpClients/OpenIddict/*` | HTTP store adapters → GeekRepository |

## Build / run

```bash
dotnet run --project GeekAPI
```

Default listen: `http://0.0.0.0:8080` (Railway sets `PORT`).
