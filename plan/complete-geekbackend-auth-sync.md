# Complete GeekBackend Identity And Sync Platform

## Decisions Locked

- Use OpenIddict for OAuth 2.1/OIDC.
- Keep the current single deployed process: `GeekAPI` is the HTTP host; `GeekApplication` and `GeekRepository` remain in-process class libraries.
- Preserve strict SoC through layers, not extra deployed services.
- Treat this system as client-to-server security. The selected architecture has no inter-service HTTP hop, so server-to-server transport security is not applicable to completion of this project.
- Bypass `ApiKeyMiddleware` for OpenIddict/OIDC endpoints such as `/connect/*` and `/.well-known/*`.
- Use Dapper-backed OpenIddict stores for auth/security persistence so `GeekRepository` remains the data-access layer for identity tables.
- Stock picker is out of scope.

## Completion Rule

This project is complete only when every requirement in this plan is implemented, connected to real PostgreSQL-backed persistence, secured according to the selected architecture, verified by build and tests, and validated against a real database-backed environment.

Every requirement must be fully implemented with real data access, real security behavior, real endpoint behavior, and passing verification before the project is considered complete.

## Target Architecture

```mermaid
flowchart LR
    Client["Electron, Browser, Mobile"] -->|"OAuth2.1/OIDC"| GeekAPI
    GeekAPI -->|"controllers, middleware, hubs"| GeekApplication
    GeekApplication -->|"interfaces and services"| GeekRepository
    GeekRepository -->|"Dapper and EF Core"| PostgreSQL
```

## Completion Requirements

1. Schema and packages

- Add OpenIddict packages to `GeekAPI/GeekAPI.csproj` and any required EF/PostgreSQL support packages.
- Implement OpenIddict persistence through Dapper-backed stores in `GeekRepository`, covering applications, authorizations, tokens, and scopes.
- Apply PostgreSQL schema for all auth/security tables through raw SQL/Dapper conventions.
- Keep EF Core limited to content tables already owned by `GeekRepository/Data`.

2. Wire OpenIddict in the single host

- Update `GeekAPI/Program.cs` with OpenIddict server and validation configuration.
- Enable authorization code + PKCE, refresh token rotation, client credentials only for trusted machine clients, revocation, userinfo, discovery, and JWKS.
- Add signing/encryption key handling suitable for development and production.
- Configure bearer authentication and authorization policies for custom API routes.

3. Fix middleware boundaries

- Update `GeekAPI/Middleware/ApiKeyMiddleware.cs` so OIDC endpoints bypass custom API-key checks.
- Move custom API protection toward `[Authorize]` bearer-token policies instead of relying on `X-API-Key` for authenticated product APIs.
- Keep public content and diagnostics routes intentionally public where already intended.

4. Implement authentication application services

- Add service implementations in `GeekApplication/Services` for user registration/login orchestration, password validation, lockout, 2FA, and audit events.
- Keep repository interfaces in `GeekApplication/Interfaces`.
- Keep data access in `GeekRepository/Repositories`.
- Avoid controller-to-repository business logic where a service boundary is needed.

5. Complete OAuth/OIDC flows

- Implement or wire endpoints for login, consent/authorization, token exchange, refresh, revocation, logout, and userinfo.
- Use exact redirect URI matching and PKCE enforcement.
- Seed/register first-party clients with explicit redirect URIs, scopes, and allowed grant types.
- Ensure tokens include `sub`, `jti`, scopes, device claims when available, and appropriate expiration.

6. Add jti replay/revocation enforcement

- Use the existing `GeekApplication/Interfaces/IJtiBlacklist.cs` and `GeekRepository/Repositories/JtiBlacklist/PostgresJtiBlacklistRepository.cs`.
- Add middleware or OpenIddict validation hooks so bearer tokens with revoked/blacklisted `jti` are rejected.
- Use PostgreSQL as the required backing store for token revocation and replay checks.

7. Complete device binding

- Extend device data/contracts for `public_key`, nonce challenge, signature verification, trusted device timestamps, and revocation semantics.
- Implement challenge-response endpoints under the auth/device area of `GeekAPI/Controllers/Auth`.
- Keep raw fingerprint generation on the Electron client; server validates, stores, challenges, and binds devices.
- Reject revoked/untrusted devices where policies require device trust.

8. Complete 2FA

- Implement RFC 6238 TOTP using a vetted package such as `OtpNet`.
- Add setup, verify, recovery-code, regenerate, trusted-device, and pending-session flows.
- Persist secrets and recovery codes using existing `user_secrets` / two-factor repository patterns.
- Enforce account lockout and constant-time password verification behavior.

9. Complete SignalR sync

- Add SignalR registration in `GeekAPI/Program.cs`.
- Add a sync hub under `GeekAPI/Hubs` protected by bearer auth and device validation.
- Use `GeekApplication/Interfaces/ISyncRepository.cs` and `GeekRepository/Repositories/SyncRepository.cs` for queue and conflict persistence.
- Track websocket sessions, deliver pending sync messages, acknowledge delivery, retry failures, and expose conflict resolution APIs.

10. Production hardening

- Add rate limiting for login, token, 2FA, device challenge, and sync endpoints.
- Add security headers, CORS tightening, audit logging for auth/device/token events, structured startup diagnostics, and health/readiness behavior.
- Remove or protect unsafe auth CRUD endpoints that expose raw repositories directly.
- Resolve nullable warnings and validate DB schema drift.

11. Verification

- Run `dotnet build` and keep the solution compiling cleanly.
- Add focused integration tests for OAuth flows, token validation, revoked `jti`, device challenge, 2FA, and SignalR sync.
- Verify with a real PostgreSQL connection before marking data-backed services complete.
- Treat any unverified external dependency or data-backed workflow as incomplete.

## Important Constraints

- Do not implement stock picker features in this task.
- Do not add separate HTTP services.
- Do not bypass OAuth/OIDC with API keys.
- Do not use in-memory or fake persistence to make the build pass.
- Do not leave required functionality unfinished or create placeholder implementations.
- Do not delete existing files or code; modify and extend in place.

