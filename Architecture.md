# GeekBackend Architecture

**Authority**: COMPREHENSIVE_IMPLEMENTATION_PLAN.md v3.4 (May 6, 2026)

---

## Overview

GeekBackend is a professional-grade OAuth 2.1/OpenID Connect identity server with real-time device synchronization. It is designed for Electron desktop applications, kiosks, mobile clients, and any OAuth 2.1-compliant application. Device fingerprinting, cryptographic keypairs, TOTP two-factor authentication, role-based access control, and WebSocket bi-directional sync are first-class citizens.

**Core Architecture**: True 3-tier design enforced by .csproj references at compile time.

---

## Solution Structure — 3-Tier Architecture

```
GeekBackend.sln
│
├── GeekAPI            ← Presentation Tier (only deployed HTTP/WSS process)
│   ├── Controllers/
│   ├── Pages/         (Razor: login, 2FA)
│   ├── Hubs/          (SignalR for real-time sync)
│   ├── Middleware/    (auth, device validation, security headers, rate limit)
│   └── Program.cs     (DI registration, middleware pipeline)
│
├── GeekApplication    ← Application Tier (class library — business logic)
│   ├── Services/      (OAuth, 2FA, device policy, password, audit)
│   ├── Interfaces/    (IService contracts)
│   ├── Entities/      (User, Device, Role, etc. — domain models)
│   └── Result<T>      (explicit status handling)
│
└── GeekRepository     ← Data Tier (class library — data access)
    ├── Repositories/  (Dapper for auth, EF Core for content)
    ├── Data/          (AppDbContext for EF Core only)
    ├── IDbConnectionFactory (Dapper connection pooling)
    └── Migrations/    (EF Core for content tables only)
```

### Project References (enforced by compiler)

```
GeekAPI → GeekApplication (allowed)
GeekApplication → GeekRepository (allowed)
GeekAPI → GeekRepository (NOT ALLOWED — must go through GeekApplication)
```

This is enforced in `.csproj` files:
```xml
<!-- GeekAPI.csproj -->
<ItemGroup>
  <ProjectReference Include="..\GeekApplication\GeekApplication.csproj" />
  <!-- GeekRepository is intentionally omitted — GeekAPI cannot bypass GeekApplication -->
</ItemGroup>

<!-- GeekApplication.csproj -->
<ItemGroup>
  <ProjectReference Include="..\GeekRepository\GeekRepository.csproj" />
</ItemGroup>

<!-- GeekRepository.csproj -->
<ItemGroup>
  <!-- No project references — pure data access -->
</ItemGroup>
```

### No Separate Shared Project

Shared contracts are distributed naturally:
- **Domain entities** (User, Device, etc.) → GeekApplication/Entities
- **Service interfaces** (IUserService, etc.) → GeekApplication/Services
- **Repository interfaces** (IUserAuthRepository, etc.) → GeekApplication/Repositories
- **DTOs & API shapes** → GeekAPI/Dtos
- **Result<T>** → GeekApplication/Results

---

## Tier Responsibilities

### Tier 3: GeekAPI (Presentation)

**Single deployed HTTP/WSS process.**

Responsibilities:
- Accept HTTP requests and WebSocket connections
- OAuth 2.1 authorization endpoints (from OpenIddict)
- 2FA challenge pages (Razor Pages)
- REST API for users, devices, admin operations
- SignalR hub for real-time sync
- Security middleware (auth, rate limiting, headers)
- Delegate all business logic to GeekApplication services

**Must NOT**:
- Access database directly (violates layer boundary)
- Contain business rules (multi-device policy, password validation, etc.)
- Reference GeekRepository classes

### Tier 2: GeekApplication (Application)

**Class library — not deployed separately. Runs inside GeekAPI process.**

Responsibilities:
- Orchestrate GeekRepository for business logic
- Implement OAuth 2.1 flows (via OpenIddict)
- Implement TOTP 2FA (RFC 6238)
- Enforce multi-device policy
- Validate passwords, check history
- Audit logging
- Result<T> wrapping (explicit status, no exceptions for business logic)

**Services**:
- TwoFactorService — TOTP setup/verify, recovery codes, trusted devices
- DevicePolicyService — multi-device enforcement, registration validation
- PasswordService — BCrypt hashing, work factor 12, history check
- AuditService — append-only audit logging
- (OAuth handled by OpenIddict + custom handlers)

**Must NOT**:
- Make HTTP calls (except to external payment processors, which bypass sync queue)
- Contain HTTP-specific logic (status codes, headers)
- Be deployed separately

### Tier 1: GeekRepository (Data)

**Class library — not deployed separately. Runs inside GeekAPI process.**

Responsibilities:
- Database connection management (IDbConnectionFactory)
- Dapper repositories for auth tables (atomic, race-condition safe)
- EF Core for content tables (relational queries)
- jti blacklist operations (PostgreSQL or Redis)
- Audit log cleanup (12-month rolling + security incident preservation)

**Data Access Split**:
- **Dapper** → users, user_secrets, user_claims, devices, device_reregistration_requests, websocket_sessions, sync_queue, sync_conflicts, jti_blacklist, audit_log, security_incidents, openiddict_*, two_factor_trusted_devices, two_factor_pending_sessions
- **EF Core** → departments, case_studies, use_cases (content tables in separate AppDbContext)

**Critical Guarantee**: AppDbContext is NOT used as a Dapper connection source. Dapper repos receive `IDbConnectionFactory`, which creates fresh `NpgsqlConnection` instances from `DATABASE_URL`.

---

## Data Access Patterns

### Dapper Repositories (Auth Tables)

```csharp
// GeekRepository/Repositories/UserAuthRepository.cs
public class UserAuthRepository : IUserAuthRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public async Task<Result<User>> CreateAsync(CreateUserRequest request)
    {
        try
        {
            // Hash password with BCrypt (work factor 12)
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(
                request.Password,
                workFactor: 12);

            using var connection = _connectionFactory.CreateConnection();
            
            var user = await connection.QueryFirstOrDefaultAsync<User>(
                @"INSERT INTO users (id, subject, username, email, password_hash, created_at)
                  VALUES (@id, @subject, @username, @email, @passwordHash, NOW())
                  RETURNING id, subject, username, email, created_at",
                new
                {
                    id = Guid.NewGuid(),
                    subject = request.Subject,
                    username = request.Username,
                    email = request.Email,
                    passwordHash
                });

            return Result<User>.Ok(user);
        }
        catch (NpgsqlException ex) when (ex.SqlState == "23505") // UNIQUE
        {
            return Result<User>.Failure("Username or email already exists");
        }
    }
}
```

**Why Dapper for Auth?**
- Atomic operations (no change tracker bloat)
- Explicit transaction control (critical for password updates, token revocation)
- Race-condition safe (no pessimistic/optimistic locking complexity)
- Direct SQL for complex auth queries (JTI blacklist, token cascades)

### EF Core Repositories (Content Tables)

```csharp
// GeekRepository/Repositories/DepartmentRepository.cs
public class DepartmentRepository : IDepartmentRepository
{
    private readonly AppDbContext _context;

    public async Task<Result<Department>> FindByIdAsync(Guid id)
    {
        var dept = await _context.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id);
        
        return dept != null
            ? Result<Department>.Ok(dept)
            : Result<Department>.NotFound($"Department {id} not found");
    }
}
```

**Why EF Core for Content?**
- Relational navigation (departments → case_studies → metrics)
- Automatic change tracking for updates
- Migration management (schema evolution via EF migrations)

---

## Deployed Infrastructure

| Component | Type | Notes |
|-----------|------|-------|
| GeekAPI | Running ASP.NET Core process | Single HTTP/WSS process with all tiers in-process |
| GeekApplication | Class library DLL | In-process, no port, no network exposure |
| GeekRepository | Class library DLL | In-process, no port, no network exposure |
| PostgreSQL | Database server | Supabase or self-hosted |
| PgBouncer | Connection pooler | Transaction mode — between GeekRepository and PostgreSQL |

---

## Multi-Application Support

GeekBackend is an OAuth 2.1/OIDC identity server. Any application that implements OAuth 2.1 can register as a client.

**Each registered application inherits:**
- Single sign-on (SSO) — auth in one app means auth in all
- Same device identity — registered devices shared across all applications
- Same 2FA enrollment — users don't re-enroll per application
- Same multi-device policy — enforced platform-wide
- Real-time sync — SignalR hub fans out across all app sessions

**Client Types**:

| Type | Auth Flow | Examples |
|------|-----------|----------|
| Electron Desktop | Authorization code + PKCE | Staff app, admin dashboard |
| Electron Kiosk | Client credentials | POS terminal (no browser) |
| Mobile | Authorization code + PKCE | iOS / Android companion |
| Web | Authorization code + PKCE | Browser apps |

**Client Registration** (no code changes required):
```
POST /api/admin/clients
Authorization: Bearer {platform.admin token}

{
  "client_id":     "my-new-app",
  "display_name":  "My App",
  "client_type":   "public",
  "grant_types":   ["authorization_code", "refresh_token"],
  "redirect_uris":  ["http://127.0.0.1"],
  "scopes":        ["openid", "profile", "offline_access"]
}
```

---

## Multi-Device Policy

### Enforcement Rules

| allow_multiple_devices | Behavior |
|------------------------|----------|
| false | Exactly one trusted device. New registration revokes all prior devices and sessions. |
| true | Up to max_devices trusted devices. Registration beyond limit returns HTTP 409. |

### Single-Device Flow (allow_multiple_devices = false)

When user registers a new device:

1. New device fingerprint + public key validated (not yet persisted)
2. All existing trusted devices: `is_trusted = false`, `revoked_at = NOW()`, `revoke_reason = 'single_device_policy'`
3. All active WebSocket sessions for those devices closed immediately via SignalR
4. All active refresh tokens revoked via OpenIddict `RevokeBySubjectAsync`
5. All `two_factor_trusted_devices` records deleted
6. New device registered as `is_trusted = true`
7. `AuditEvent.DeviceReplacedByPolicy` written
8. User notified via email

### Admin Controls

```
PATCH /api/admin/users/{userId}/device-policy
Authorization: Bearer {platform.admin token}

{
  "allow_multiple_devices": true,
  "max_devices": 3
}
```

Tightening the policy (e.g., from 5 to 3 devices) triggers immediate enforcement on most recently seen devices.

---

## Two-Factor Authentication (TOTP)

### Setup Flow

1. User requests 2FA setup
   ```
   GET /api/2fa/setup
   ← { qr_code_uri, backup_codes[] }
   ```

2. User scans QR code in authenticator app

3. User enters first TOTP code to confirm
   ```
   POST /api/2fa/setup/confirm { totp_code: "123456" }
   ```

4. Server validates code, activates 2FA, returns 10 recovery codes (shown **once only**)

5. Recovery codes stored as BCrypt hashes — never plaintext

### Login Flow with 2FA (Authorization Code + PKCE)

**Critical**: Resource Owner Password Credentials grant (`grant_type: "password"`) is explicitly **prohibited** by OAuth 2.1. All human user authentication uses authorization code + PKCE.

Flow:
1. Electron opens system browser with authorization request (includes PKCE challenge)
2. GeekAPI prompts for username + password
3. Constant-time credential validation
4. If 2FA enabled + device not trusted: redirect to `/connect/2fa?session=<encrypted_id>`
5. User enters TOTP code (6 digits) or recovery code (XXXX-XXXX)
6. Server routes automatically by format
7. Invalid → increment failure, re-show
8. Failures ≥ 5 → lockout, `AuditEvent.TwoFactorLockout`
9. Recovery code → marked single-use, warning if < 3 remain
10. Valid → optional "Trust this device for N days" checkbox
11. Authorization code issued, Electron receives it in loopback callback

### Trusted Devices for 2FA

- Default trust window: 30 days (configurable via `two_factor_trust_days`)
- Per-device — trusting one device doesn't affect others
- Revocation removes 2FA trust record
- Stored in `two_factor_trusted_devices` table with expiry

### Recovery Codes

- 10 codes generated at setup, shown **exactly once**
- Format: XXXX-XXXX (alphanumeric, no ambiguous chars)
- Single-use — permanently invalidated on use
- Warning shown when < 3 remain
- User can regenerate all 10 (requires valid TOTP or admin override)
- Regeneration invalidates all existing codes immediately

### Password Change

```
POST /api/users/password-change
Authorization: Bearer {access_token}

{
  "current_password": "...",
  "new_password": "..."
}
```

- Validate access token
- BCrypt verify current password (generic error if invalid)
- Check against last 10 stored hashes — reject if reuse
- Hash new password (work factor 12)
- Store in history, rotate
- Revoke all refresh tokens
- Close all WebSocket sessions
- Current session continues (user stays logged in)
- All other devices force re-authentication
- `AuditEvent.PasswordChanged` written with actor identity + IP

---

## jti Replay Detection (Token Reuse Prevention)

### Interface

```csharp
// GeekApplication/Interfaces/IJtiBlacklist.cs
public interface IJtiBlacklist
{
    /// <summary>
    /// Atomically checks and blacklists a jti.
    /// Returns true  = first use (allow).
    /// Returns false = already seen (replay — reject).
    /// </summary>
    Task<bool> CheckAndBlacklistAsync(
        string jti,
        DateTimeOffset expiresAt,
        CancellationToken ct = default);

    /// <summary>
    /// Purge expired entries (PostgreSQL only; no-op for Redis).
    /// Called nightly via background service.
    /// </summary>
    Task PurgeExpiredAsync(CancellationToken ct = default);
}
```

### V1: PostgreSQL Implementation (Shipped)

```csharp
// GeekRepository/Auth/PostgresJtiBlacklist.cs
public sealed class PostgresJtiBlacklist : IJtiBlacklist
{
    private readonly IDbConnectionFactory _db;

    public async Task<bool> CheckAndBlacklistAsync(
        string jti, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO jti_blacklist (jti, expires_at)
            VALUES (@jti, @expiresAt)
            ON CONFLICT (jti) DO NOTHING
            RETURNING jti;
            """;

        using var conn = _db.CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<string>(sql,
            new { jti, expiresAt });

        return result != null; // null = conflict = replay detected
    }

    public async Task PurgeExpiredAsync(CancellationToken ct = default)
    {
        const string sql = "DELETE FROM jti_blacklist WHERE expires_at < NOW();";
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(sql);
    }
}
```

**Concurrency guarantee**: `INSERT ... ON CONFLICT DO NOTHING RETURNING` is atomic at the PostgreSQL engine level. Two simultaneous requests with the same jti cannot both succeed — only one insert completes.

### Future: Redis Implementation (Written, Not Registered)

Activated by changing one line in Program.cs when traffic meets trigger conditions:

- 500+ concurrent WebSocket connections
- CPU/memory ≥ 80% sustained
- GeekAPI running on >1 instance
- jti query P95 > 10ms under load
- 10,000+ token validations/day sustained

```csharp
// GeekRepository/Auth/RedisJtiBlacklist.cs
// To activate: Program.cs registration change only
public sealed class RedisJtiBlacklist : IJtiBlacklist
{
    private readonly IConnectionMultiplexer _redis;

    public async Task<bool> CheckAndBlacklistAsync(
        string jti, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var ttl = expiresAt - DateTimeOffset.UtcNow;
        return await db.StringSetAsync($"jti:{jti}", "1", ttl, When.NotExists);
    }

    public Task PurgeExpiredAsync(CancellationToken ct = default)
        => Task.CompletedTask; // Redis native TTL
}
```

### Middleware

```csharp
// GeekAPI/Middleware/JtiValidationMiddleware.cs
public async Task InvokeAsync(HttpContext context, IJtiBlacklist blacklist)
{
    var jti = context.User.FindFirst("jti")?.Value;
    if (string.IsNullOrEmpty(jti))
    {
        context.Response.StatusCode = 401;
        return;
    }

    var exp = context.User.FindFirst("exp")!.Value;
    var expiresAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(exp));

    var allowed = await blacklist.CheckAndBlacklistAsync(jti, expiresAt);
    if (!allowed)
    {
        await _audit.LogAsync(AuditEvent.JtiReplayDetected, jti);
        context.Response.StatusCode = 401;
        return;
    }

    await _next(context);
}
```

---

## OAuth 2.1 & OpenID Connect (OpenIddict)

### Client Registration Examples

**Electron Desktop** (public client, PKCE required):
```csharp
options.AddApplication(new OpenIddictApplicationDescriptor
{
    ClientId = "geek-electron",
    ClientType = ClientTypes.Public,
    Permissions = {
        Permissions.GrantTypes.AuthorizationCode,
        Permissions.GrantTypes.RefreshToken,
        Permissions.ResponseTypes.Code,
        Permissions.Scopes.OpenId,
        Permissions.Scopes.Profile,
        Permissions.Scopes.OfflineAccess,
        Permissions.Prefixes.Scope + "devices.manage",
        Permissions.Prefixes.Scope + "sync.read",
        Permissions.Prefixes.RedirectUri + "http://127.0.0.1",
    },
    Requirements = {
        Requirements.Features.ProofKeyForCodeExchange, // MANDATORY
    }
});
```

**Kiosk** (confidential client, client credentials only):
```csharp
options.AddApplication(new OpenIddictApplicationDescriptor
{
    ClientId = "geek-kiosk-store1",
    ClientType = ClientTypes.Confidential,
    ClientSecret = Environment.GetEnvironmentVariable("KIOSK_STORE1_SECRET"),
    Permissions = {
        Permissions.GrantTypes.ClientCredentials,
        Permissions.Prefixes.Scope + "pos.read",
        Permissions.Prefixes.Scope + "pos.write",
    }
});
```

### Kiosk Authentication (Client Credentials)

Kiosks are Electron-based but authenticate as **machines**, not humans:
- No browser flow
- No TOTP requirement (exempt)
- `client_secret` stored in OS keychain (written during provisioning)
- No user interaction

```bash
POST /connect/token
{
  "grant_type": "client_credentials",
  "client_id": "geek-kiosk-store1",
  "client_secret": "<from OS keychain>"
}
```

Response: Access token with narrow scopes (`pos.read`, `pos.write`)

**Kiosk Security Requirements**:
- OS boots directly into Electron app (no desktop)
- No keyboard shortcuts to exit (Alt+F4, Win key disabled)
- No browser installed
- Auto-login under dedicated locked system account
- `client_secret` in system keychain
- Secret rotation via admin API (operator never involved)
- Kiosk detects 401 → displays "Needs reprovisioning"

### Refresh Token Rotation & Theft Detection

```csharp
options.UseRollingRefreshTokens();
options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));
options.SetRefreshTokenReuseLeeway(TimeSpan.FromSeconds(30));
```

Theft detection:
```csharp
public class RefreshTokenReuseHandler
    : IOpenIddictServerHandler<HandleTokenRequestContext>
{
    public async ValueTask HandleAsync(HandleTokenRequestContext context)
    {
        if (context.Transaction.GetProperty<bool>("token_reuse_detected"))
        {
            var subject = context.Principal?.GetClaim(Claims.Subject);
            await _tokenManager.RevokeBySubjectAsync(subject!);
            await _audit.LogAsync(AuditEvent.RefreshTokenTheftDetected, subject);
        }
    }
}
```

On theft: **all tokens for that user are revoked**. User must re-authenticate everywhere.

### Token Lifecycle

| Token | TTL | Storage | Rotation | Revocation |
|-------|-----|---------|----------|-----------|
| Access (user) | 15 min | Memory | On refresh | Short TTL |
| Access (kiosk) | 15 min | Memory | Re-auth on expiry | Short TTL |
| Refresh | 30 days | OS Keychain (keytar) | Every use (reference + reuse leeway) | Explicit or theft-detected |
| ID | 15 min | Memory | Not rotated | Logout clears |

### OpenIddict signing key rotation (production runbook)

GeekAPI loads signing (and optional encryption) certificates from environment via `OpenIddictCertificateLoader`:

| Variable | Format |
|----------|--------|
| `OPENIDDICT_SIGNING_CERT` | PEM text, file path, or base64 PKCS#12 |
| `OPENIDDICT_SIGNING_CERT_PASSWORD` | PFX password when using PKCS#12 |
| `OPENIDDICT_ENCRYPTION_CERT` | Optional; defaults to signing cert |

**Rotation procedure (zero-downtime):**

1. Generate new RSA or ECDSA keypair; export PKCS#12 or PEM.
2. Set `OPENIDDICT_SIGNING_CERT` (and password if needed) on GeekAPI in Railway.
3. Redeploy GeekAPI. New tokens sign with the new key; JWKS at `/.well-known/jwks.json` updates on restart.
4. Keep the previous key published in JWKS for at least the longest access-token TTL (15 minutes) if doing dual-key rotation — OpenIddict 7 single-cert config rotates immediately; plan maintenance window or accept 15-minute overlap where old access tokens may fail validation after cutover.
5. Resource servers using introspection (`geek-resource-server`) are unaffected by signing rotation.
6. Never commit private keys to git. Store only in Railway secrets.

**Composition root note:** OpenIddict store wiring and certificate loading live in `GeekAPI` (composition root exception documented here). `GeekApplication` remains HTTP- and database-free.

---

## Device Identity & Management

### Fingerprinting Strategy

| Signal | Reliability | Role |
|--------|-------------|------|
| Cryptographic keypair | HIGHEST | Primary identity. Private key never transmitted. |
| OS machine ID (`node-machine-id`) | HIGH | Cross-platform, changes on OS reinstall |
| BIOS UUID | LOW | Optional. Unavailable on macOS/Linux/VMs. |
| Platform + arch | HIGH | Supplementary |

```
device_fingerprint = SHA-256(machineId + biosUuid_or_empty + platform)
```

### Unsupported Environments

Virtual machines and Docker containers are **explicitly rejected** at registration:

```json
{
  "error": "unsupported_environment",
  "message": "Virtual machines and container environments are not supported. Persistent OS keychain required."
}
```

**Detection**:
- Known VM machineId patterns
- BIOS UUID all-zeros
- Hypervisor signatures in system info

`AuditEvent.RegistrationRejectedUnsupportedEnvironment` written on every attempt.

### First-Run Registration

1. Generate ECDSA P-256 keypair
2. Store private key in OS keychain via keytar
3. Collect signals → compute `device_fingerprint`
4. Validate environment (reject VM/Docker)
5. Check `allow_multiple_devices` and `max_devices` policy
6. `POST /api/devices/register` with fingerprint + public key
7. Server stores device, sets trust status per policy
8. Device shown in user's management UI

### Challenge-Response Authentication

1. `POST /api/devices/challenge` → server issues `nonce` (60s TTL, single-use, stored in PostgreSQL)
2. Electron signs nonce: `signature = privateKey.sign(nonce)`
3. `POST /api/devices/verify` with fingerprint, nonce, signature
4. Server verifies signature against stored `public_key`
5. Nonce deleted immediately on use
6. On success: device authenticated, session registered

### Device Re-Registration

Triggered when fingerprint no longer matches or keypair missing from keychain.

1. New fingerprint + keypair registered as unrecognized device
2. User shown "New device detected" (not silent)
3. User selects reason: OS reinstall / new machine / hardware change
4. Auth via trusted device or email OTP
5. Admin approval if policy requires
6. Old device revoked, sessions closed, tokens revoked, 2FA trust removed
7. New device trusted

**Orphan cleanup**:
- 90 days old → flagged for review
- 180 days old → auto-revoked (configurable)

---

## Real-Time Synchronization

### Scope

Sync queue handles **identity and session state only**:
- Device trust changes
- Presence updates
- User preference changes

It is **NOT** a general-purpose message queue. It is NOT for application data (POS transactions, etc.). Payment processing uses separate platforms with OAuth 2.1 authorization, not the sync queue.

### SignalR Hub

```csharp
[Authorize]
[RequireDeviceAuthentication]
public class SyncHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User.FindFirst("sub")!.Value;
        var deviceId = Context.GetHttpContext()!
            .Request.Headers["X-Device-Fingerprint"].ToString();

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        await _sessions.RegisterAsync(Context.ConnectionId, userId, deviceId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? ex)
    {
        await _sessions.RemoveAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(ex);
    }
}
```

### Conflict Resolution

**Last-write-wins** via `updated_at`:

| Scenario | Strategy | Outcome |
|----------|----------|---------|
| No conflict | Direct apply | Immediate |
| Remote newer | Compare `updated_at` | Remote applied |
| Local newer | Compare `updated_at` | Local retained |
| Identical timestamp | Alphabetical device_id tiebreak | Consistent |

Offline queue delivery: ordered replay by `priority DESC, created_at ASC`.

### Redis Backplane (Future)

Added when **any** of these trigger:
- 500+ concurrent WebSocket connections
- CPU/memory ≥ 80% sustained
- GeekAPI on >1 instance simultaneously
- jti query P95 > 10ms under load
- 10,000+ token validations/day

---

## Electron Client Security (Non-Negotiable Baseline)

### BrowserWindow Configuration

```javascript
const win = new BrowserWindow({
  webPreferences: {
    contextIsolation:            true,   // MANDATORY
    nodeIntegration:             false,  // MANDATORY
    sandbox:                     true,
    webSecurity:                 true,   // NEVER set false
    allowRunningInsecureContent: false,
    experimentalFeatures:        false,
  }
});
```

### OAuth PKCE Flow (Desktop Only)

**REJECTED**: Custom URI schemes (`geekapp://`) — any app can register and steal codes.

**REQUIRED**: RFC 8252 loopback redirect on random OS-assigned port.

```javascript
async function startAuthFlow(): Promise<string> {
  const server = http.createServer();
  const port = await getRandomPort();
  server.listen(port, '127.0.0.1');

  const codeVerifier = crypto.randomBytes(64).toString('base64url');
  const codeChallenge = crypto.createHash('sha256')
    .update(codeVerifier).digest('base64url');
  const state = crypto.randomBytes(32).toString('hex');

  const opened = await shell.openExternal(buildAuthUrl({
    codeChallenge, state,
    redirectUri: `http://127.0.0.1:${port}/callback`
  }));

  // Fallback if no browser configured
  if (!opened) {
    const fallback = new BrowserWindow({...});
    fallback.loadURL(`data:text/html,<p>Copy this URL...</p>`);
  }

  return new Promise((resolve, reject) => {
    // Wait for callback...
  });
}
```

**BANNED**: `new BrowserWindow().loadURL(authUrl)` — never use embedded WebView for auth.

**NOTE**: Kiosks never call `startAuthFlow()` — they use client credentials (no browser).

### Token Storage

| Token | Storage | Process | Rationale |
|-------|---------|---------|-----------|
| Private Key | OS Keychain (keytar) | Main only | Never rotates. OS ACL protected. |
| Refresh Token | OS Keychain (keytar) | Main only | Long-lived. Survives restart. |
| Kiosk client_secret | OS Keychain (keytar) | Main only | Written at provisioning only. |
| Access Token | In-memory | Main only | Short TTL. Lost on restart. |
| ID Token | In-memory | Main only | Display only. Short-lived. |

### IPC Security

Renderer process **never holds tokens**.

```javascript
// preload.ts
contextBridge.exposeInMainWorld('auth', {
  getUserProfile: () => ipcRenderer.invoke('auth:getUserProfile'),
  getDeviceInfo: () => ipcRenderer.invoke('auth:getDeviceInfo'),
  logout: () => ipcRenderer.invoke('auth:logout'),
  isAuthenticated: () => ipcRenderer.invoke('auth:isAuthenticated'),
});

// ❌ Never exposed: getAccessToken, getRefreshToken, keytar, submitPassword
```

Main process handles token operations:
```javascript
// main.ts
ipcMain.handle('auth:getUserProfile', async () => {
  const token = await tokenStore.getValidAccessToken();
  const resp = await fetch('https://geekoauth.../connect/userinfo', {
    headers: { Authorization: `Bearer ${token}` }
  });
  return resp.json(); // Returns profile, NOT token
});
```

---

## Security Hardening

### HTTP Security Headers

```csharp
app.Use(async (context, next) => {
    var h = context.Response.Headers;
    h["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains; preload";
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; " +
        "connect-src 'self' wss://geekapi.geekatyourspot.com; frame-ancestors 'none'";
    await next();
});
```

### Rate Limiting

| Endpoint | Window | Max | On Breach |
|----------|--------|-----|-----------|
| POST /connect/token | 15 min | 5 | 15 min lockout + alert |
| POST /api/users/register | 1 hour | 3 | 1 hour lockout |
| POST /api/users/password-change | 1 hour | 5 | 1 hour lockout + alert |
| POST /api/devices/challenge | 1 min | 10 | 5 min lockout |
| POST /connect/2fa | 5 min | 5 | Account lockout + alert |
| All authenticated | 1 min | 100 | 429 |
| All unauthenticated | 1 min | 20 | 429 + IP flag |

### Password Security

- **BCrypt** work factor 12 (increase to 13 when P95 hash time > 100ms)
- Minimum 12 characters: 1 upper, 1 lower, 1 digit, 1 symbol
- Last 10 hashes stored in `user_secrets.password_history` — prevent reuse
- No plaintext ever stored or logged

### Structured Logging (Serilog)

```csharp
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("application", "GeekAPI")
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.File(
        new JsonFormatter(),
        path: "logs/geekapi-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();
```

Log event schema:
```json
{
  "timestamp": "2026-05-06T14:23:01.000Z",
  "level": "Information",
  "application": "GeekAPI",
  "event_type": "DeviceRegistered",
  "user_id": "uuid",
  "device_id": "uuid",
  "ip": "192.168.1.1",
  "message": "Device registered successfully"
}
```

Console + file sinks. 30-day rolling. No external service.

### Audit Logging

Append-only. Rows never updated or deleted.

**Retention**: 12 months rolling. Security incidents preserved indefinitely.

**Events by category**:
- Authentication (login, logout, token refresh, 2FA events)
- 2FA Lifecycle (setup, disable, recovery codes, trusted devices)
- Authorization (token failures, scope rejection, jti replay)
- Device (registration, VM rejection, trust, challenge, re-registration)
- Token (issuance, revocation, theft detection)
- Password (change, reuse rejection)
- Multi-Device (limit exceeded, policy replacement, revocation)
- Administrative (user lock, client registration, policy changes)

---

## Build & Deployment

### Build
```bash
dotnet build GEEKBACKEND.slnx
```

### Run (Development)
```bash
cd GeekAPI
dotnet run
```

### Database Setup
```bash
# Apply PostgreSQL schema (raw SQL)
psql -U postgres geekbackend < schema.sql

# Apply EF Core migrations (content tables)
cd GeekRepository
dotnet ef database update
```

### Environment Variables
```bash
DATABASE_URL=postgresql://user:pass@localhost/geekbackend
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=https://+:5001
# See COMPREHENSIVE_IMPLEMENTATION_PLAN.md for complete list
```

---

## Implementation Status

✅ **Completed**:
- 3-tier architecture (GeekAPI, GeekApplication, GeekRepository)
- Dapper repositories for auth tables
- EF Core for content tables
- DeviceOauth entity with device fingerprinting
- Multi-device policy framework
- PostgreSQL schema (9 migrations)

⏳ **In Progress**:
- OpenIddict configuration (OAuth 2.1, OIDC)
- TwoFactorService (TOTP, recovery codes, trusted devices)
- DevicePolicyService (enforce policy on registration)
- PKCE flow (Electron client)
- SignalR hub (real-time sync)

🔲 **Pending**:
- 2FA challenge pages (Razor Pages)
- Kiosk client credentials flow
- Device challenge-response
- Conflict resolution (sync_conflicts)
- Audit cleanup service (12-month rolling)
- Load testing (1,000+ WebSocket)
- Security penetration test

---

**Authority**: COMPREHENSIVE_IMPLEMENTATION_PLAN.md v3.4 (May 6, 2026)  
**Last Updated**: May 7, 2026
