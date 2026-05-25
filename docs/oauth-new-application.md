# Register a new OAuth application (GeekAPI)

GeekAPI (`https://api.geekatyourspot.com`) is the **only** OAuth 2.1 issuer for Geek products. Every new app (SPA, Electron, mobile, backend) is an OAuth **client**; your API is a **resource server** that validates tokens from GeekAPI.

## 1. Choose a client type

| Type | Use when | Grant | Secret |
|------|----------|-------|--------|
| **Public + PKCE** | Browser, Electron, mobile | Authorization code | None (PKCE required) |
| **Confidential** | Server-to-server, cron, MCP | Client credentials | Yes |
| **Confidential introspection** | Your API validates user tokens | Client credentials → introspect | Yes |

## 2. Register the client

### Option A — Admin API (no deploy)

Requires a bearer token whose principal has role `admin`.

```http
POST /api/admin/clients
Authorization: Bearer {admin_access_token}
Content-Type: application/json

{
  "clientId": "my-new-app",
  "displayName": "My New App",
  "isPublic": true,
  "clientSecret": null,
  "redirectUris": [
    "http://127.0.0.1:5173/callback",
    "myapp://callback"
  ],
  "scopes": null,
  "allowIntrospection": false
}
```

Public clients automatically receive: authorization code, refresh token, PKCE, `openid` / `profile` / `email` / `offline_access` permissions.

Confidential machine client:

```json
{
  "clientId": "my-new-app-backend",
  "displayName": "My New App API",
  "isPublic": false,
  "clientSecret": "generate-a-long-random-secret",
  "redirectUris": null,
  "scopes": ["mcp.tools"],
  "allowIntrospection": false
}
```

Store `clientSecret` in your host's environment variables (Railway, Render, etc.) — never in source control.

### Option B — First-party seed (Geek-owned apps)

Add the client to `GeekAPI/Infrastructure/OpenIddictClientSeeder.cs` and deploy GeekAPI. Use this for long-lived Geek products (same as `geek-seo-electron`).

## 3. Configure your application

Set these in the **client app** (not GeekAPI):

| Variable | Example |
|----------|---------|
| `AUTH_SERVER_URL` | `https://api.geekatyourspot.com` |
| `OAUTH_CLIENT_ID` | `my-new-app` |
| `OAUTH_REDIRECT_URI` | `http://127.0.0.1:5173/callback` |

### Authorization code + PKCE (public clients)

1. Discover metadata: `GET {AUTH_SERVER_URL}/.well-known/openid-configuration`
2. Generate `code_verifier` (43–128 chars) and `code_challenge = BASE64URL(SHA256(verifier))`.
3. Redirect the user to:
   ```
   GET {AUTH_SERVER_URL}/connect/authorize
     ?client_id={OAUTH_CLIENT_ID}
     &response_type=code
     &scope=openid%20offline_access
     &redirect_uri={OAUTH_REDIRECT_URI}
     &code_challenge={CHALLENGE}
     &code_challenge_method=S256
   ```
4. User signs in at `/Account/Login` (and `/Account/TwoFactor` if enabled).
5. Exchange the `code` at `POST {AUTH_SERVER_URL}/connect/token` with `code_verifier`.

See [README.md](../README.md) — **Manual PKCE smoke test** for curl examples.

### Client credentials (confidential)

```bash
curl -sS -X POST "$AUTH_SERVER_URL/connect/token" \
  -d "grant_type=client_credentials" \
  -d "client_id=my-new-app-backend" \
  -d "client_secret=$MY_APP_CLIENT_SECRET" \
  -d "scope=mcp.tools"
```

## 4. Protect your API (resource server)

In your **new project's** ASP.NET (or Node via introspection) API:

```csharp
services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(Environment.GetEnvironmentVariable("AUTH_SERVER_URL"));
        options.UseIntrospection()
            .SetClientId("geek-resource-server") // or your introspection client
            .SetClientSecret(Environment.GetEnvironmentVariable("GEEK_RESOURCE_SERVER_SECRET"));
        options.UseAspNetCore();
    });
```

Introspection returns `active: false` when GeekAPI revokes a token or blacklists a JTI — you do not implement a separate blacklist.

For `@geek/auth-client` (TypeScript monorepo), use JWT validation against JWKS:

```
GET {AUTH_SERVER_URL}/.well-known/jwks.json
```

## 5. CORS (browser clients only)

Add your web origin to GeekAPI Railway env:

```
CORS_ORIGINS=https://myapp.example.com,http://127.0.0.1:5173
```

Redeploy GeekAPI after changing CORS.

## 6. Verify

```bash
# Issuer health
curl -sS "$AUTH_SERVER_URL/health"

# Discovery
curl -sS "$AUTH_SERVER_URL/.well-known/openid-configuration"

# Automated tests (optional, needs PostgreSQL)
export TEST_DATABASE_URL="postgresql://..."
dotnet test GEEKBACKEND.slnx --filter OAuthEndToEnd
```

## Checklist

- [ ] Client registered (`openiddict_applications` row exists)
- [ ] Redirect URI matches exactly (including trailing slash policy)
- [ ] PKCE used for public clients
- [ ] `AUTH_SERVER_URL` points to GeekAPI, not GeekRepository
- [ ] Resource server introspection or JWKS validation configured
- [ ] CORS origin added for browser apps
