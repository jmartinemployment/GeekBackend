# GeekBackend

Enterprise authentication & authorization platform for Geek applications. OAuth 2.1, PKCE, TOTP 2FA, multi-device device management, real-time session sync, and zero-trust device challenge-response.

**Status:** OAuth 2.1 issuer live on GeekAPI (OpenIddict 7). PostgreSQL via GeekRepository (Dapper). Deploy and verify with `/.well-known/openid-configuration`.

## Architecture

**3-Tier ASP.NET Core:**

```
GeekAPI                  [HTTP/WebSocket presentation]
  ↓ (references only)
GeekApplication          [Application logic, domain contracts, in-process]
  ↓ (references only)
GeekRepository           [Data access: Dapper + EF Core, in-process]
  ↓
PostgreSQL              [Auth tables via Dapper; content via EF Core]
```

Layer boundaries enforced at `.csproj` reference level. See [Architecture.md](./Architecture.md) for complete specification (OAuth 2.1, TOTP, device fingerprinting, multi-device policy, jti replay detection, real-time sync).

## Tech Stack

| Layer | Technology | Notes |
|-------|-----------|-------|
| **Runtime** | .NET 10 / ASP.NET Core | Minimal APIs / Controllers |
| **Auth data** | Dapper 2.x | Atomic operations, race-condition safe |
| **Content data** | EF Core 10 | Relational, migrations |
| **Database** | PostgreSQL 16 | Self-hosted; Railway deployment |
| **Real-time** | SignalR (WebSocket) | Session sync, last-write-wins |
| **2FA** | TOTP (RFC 6238) | Trusted devices (30-day window) |
| **Device mgmt** | SHA-256 fingerprint + challenge-response | FIPS 140-2 compatible |
| **Logging** | Serilog | JSON, rolling 30-day, console + file |
| **Testing** | xUnit + Moq | — |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 16+ (local or Railway)
- `dotnet-ef` CLI tools:
  ```bash
  dotnet tool install --global dotnet-ef
  ```

### Setup

1. **Clone repo:**
   ```bash
   git clone <repo>
   cd GeekBackend
   ```

2. **Create `.env` (solution root):**
   ```bash
   DATABASE_URL="postgresql://user:pass@localhost:5432/geekbackend"
   DIRECT_URL="postgresql://user:pass@localhost:5432/geekbackend"
   JWT_KEY="<base64-encoded-256-bit-key>"
   ENCRYPTION_KEY="<base64-encoded-256-bit-key>"
   ```

3. **Restore packages:**
   ```bash
   dotnet restore GeekBackend.sln
   ```

4. **Apply migrations:**
   ```bash
   dotnet ef database update --project GeekRepository --startup-project GeekAPI
   ```

5. **Run API:**
   ```bash
   dotnet run --project GeekAPI
   # API listens: http://localhost:5000 (HTTP), ws://localhost:5000 (WebSocket)
   ```

### Build & Test

```bash
# Build
dotnet build GeekBackend.sln

# Run all tests
dotnet test GeekBackend.sln

# Run specific test class
dotnet test --filter GeekRepository.Tests.UserAuthRepositoryTests

# Code coverage
dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover

# Lint check
dotnet format --verify-no-changes GeekBackend.sln
```

## Project Structure

```
GeekBackend.sln
├── GeekAPI/                    # HTTP/WebSocket presentation layer
│   ├── Controllers/            # Auth, content, system endpoints
│   ├── Hubs/                   # SignalR session sync hub
│   ├── Middleware/             # Auth, CORS, rate limiting, security headers
│   ├── Dtos/                   # Request/response contracts
│   ├── Program.cs              # DI, middleware setup
│   └── appsettings.*.json      # Config per environment
│
├── GeekApplication/            # Application logic (domain contracts)
│   ├── Entities/               # User, Device, OIDC, RBAC, AuditLog entities
│   ├── Models/                 # Requests, responses, DTOs
│   ├── Interfaces/             # IUserService, IDeviceService, etc
│   └── Constants/              # Domain constants, enums
│
└── GeekRepository/             # Data access layer
    ├── Data/
    │   ├── AppDbContext.cs     # EF Core context (content tables only)
    │   └── DesignTimeFactory.cs # Migrations helper
    ├── Migrations/             # EF code-first migrations
    ├── Repositories/
    │   ├── Dapper/             # Atomic auth operations (users, devices, oauth, oidc, audit, rbac)
    │   └── EF/                 # Relational content (departments, case_studies, use_cases)
    └── PostgreSchema.txt       # DDL for all tables
```

## Implementation Status

| Component | Status | Owner | ETA |
|-----------|--------|-------|-----|
| **Entities & Repos** | ✅ Complete | — | — |
| **AppDbContext + Migrations** | ✅ Complete | — | — |
| **DeviceOauth fingerprint** | ✅ Complete | — | — |
| **User auth (register/login)** | ✅ Implemented | GeekAPI + GeekRepository | Razor `/Account/Login` |
| **TOTP 2FA (setup/verify)** | ✅ Implemented | GeekAPI | `/Account/TwoFactor`, `/api/auth/2fa/*` |
| **Device challenge-response** | ✅ Implemented | GeekRepository | ECDSA challenge/verify |
| **Multi-device policy enforcement** | ✅ Implemented | GeekApplication | `DevicePolicy` + repo |
| **OAuth 2.1 + OpenIddict** | ✅ Implemented | GeekAPI | `/.well-known/openid-configuration` |
| **Refresh token rotation** | ✅ Implemented | GeekAPI | Reference refresh + reuse handler |
| **jti replay detection** | ✅ Implemented | GeekRepository | `jti_blacklist` + middleware |
| **Real-time SignalR sync** | ✅ Implemented | GeekAPI | `/hubs/sync` |
| **Security hardening** | ✅ Implemented | GeekAPI | Headers, rate limits, CORS |
| **Electron integration** | 🔄 Client-side | — | Point apps at GeekAPI issuer |

## Key Concepts

### Data Access Split

**Dapper (Auth tables):**
- `users`, `devices_oauth`, `oauth_clients`, `oauth_tokens`, `oidc_*`, `rbac_*`, `audit_log`
- Atomic operations, direct SQL, race-condition safe
- Repositories: `UserAuthRepository`, `DeviceRepository`, `OAuthClientRepository`, etc.

**EF Core (Content tables):**
- `departments`, `case_studies`, `use_cases`, related lookup tables
- Relational navigation, migrations, change tracking
- Repositories: `DepartmentRepository`, `CaseStudyRepository`, `UseCaseRepository`

### Device Fingerprinting

Composite unique identifier per user+device:
```
fingerprint = SHA-256(machineId || biosUuid_or_empty || platform)
```

Immutable. Used for device authentication challenge-response and multi-device policy enforcement.

### Multi-Device Policy

- **allow_multiple_devices:** boolean flag per user
- **max_devices:** default 5 per user
- **Single-device mode:** Registering new device revokes ALL existing devices; requires re-login on all other devices
- **Limit exceeded:** HTTP 409 Conflict; user must revoke another device or request limit increase

### TOTP 2FA

- **Setup:** QR code + recovery codes (10 BCrypt hashes, single-use)
- **Login:** 6-digit TOTP code required at authorization endpoint (TwoFactorAuthorizationHandler intercept)
- **Trusted devices:** 30-day window per device (configurable); skip 2FA within window
- **Account lockout:** 5 failed attempts → 24-hour lockout

### OAuth 2.1 Compliance

- **Authorization code flow:** Human clients (Electron, web)
- **PKCE required:** All public clients
- **Refresh token rotation:** New refresh token per use; old invalidated immediately
- **Resource owner password credentials:** **EXPLICITLY PROHIBITED** (deprecated in OAuth 2.1)
- **Client credentials:** Kiosk/system clients (short-lived, non-interactive)

### Real-Time Sync (SignalR)

- **Scope:** Identity/session state only (user profile, device presence, 2FA status)
- **Not a general-purpose queue:** Dedicated sync service for auth state
- **Conflict resolution:** Last-write-wins via `updated_at` timestamp
- **Offline queue:** Delivery with priority ordering; 24-hour TTL
- **Authentication:** Bearer token + device fingerprint validation

## Configuration

### Environment Variables

```bash
# Database
DATABASE_URL="postgresql://..."           # PgBouncer pooler (runtime)
DIRECT_URL="postgresql://..."             # Direct connection (migrations only)

# Security
JWT_KEY="base64(256-bit-key)"             # Signing key for JWTs
ENCRYPTION_KEY="base64(256-bit-key)"      # Symmetric encryption for sensitive data
BCRYPT_WORK_FACTOR=12                     # Password hashing cost

# TOTP
TOTP_ISSUER="Geek"                        # QR code issuer name
TOTP_WINDOW_SIZE=1                        # ±1 time step tolerance (30-second window)
TRUSTED_DEVICE_WINDOW_DAYS=30              # 2FA bypass window

# Rate Limiting
RATE_LIMIT_LOGIN_PER_MINUTE=5              # Failed login attempts
RATE_LIMIT_TOKEN_PER_HOUR=100              # Token endpoint requests
RATE_LIMIT_GENERAL_PER_SECOND=10           # Global rate limit

# Logging
LOG_LEVEL="Information"                    # Debug, Information, Warning, Error
SERILOG_MIN_LEVEL="Information"

# GeekAPI issuer (required in production)
AUTH_SERVER_URL="https://api.geekatyourspot.com"
REPO_URL="https://your-geekrepository.up.railway.app"
GEEK_API_CLIENT_SECRET="geekapi-client-credentials-secret"
AUTH_SERVER_URL="https://api.geekatyourspot.com"
# GeekRepository also needs AUTH_SERVER_URL (same issuer) for JWT validation.
# See docs/geekrepository-oauth-access.md
GEEK_BACKEND_API_KEY="api-key-for-product-routes"
GEEK_WEBSITE_CLIENT_SECRET="confidential-client-secret"
GEEK_RESOURCE_SERVER_SECRET="introspection-client-secret"
CORS_ORIGINS="https://seo.geekatyourspot.com,http://127.0.0.1"
OPENIDDICT_SIGNING_CERT="path-or-pem-or-base64-pfx"
OPENIDDICT_SIGNING_CERT_PASSWORD="optional-pfx-password"

# Integration tests (optional)
TEST_DATABASE_URL="postgresql://..."
TEST_REPO_URL="http://127.0.0.1:5050"
```

## New OAuth application

Step-by-step guide to register and wire a new app: [docs/oauth-new-application.md](docs/oauth-new-application.md).

## Manual PKCE smoke test

Use a single canonical issuer (`AUTH_SERVER_URL`). Example with production:

```bash
export ISSUER="https://api.geekatyourspot.com"
curl -sS "$ISSUER/.well-known/openid-configuration" | head
curl -sS "$ISSUER/health"
```

**Authorization code + PKCE (browser):**

1. Generate `code_verifier` (43–128 chars) and `code_challenge = BASE64URL(SHA256(verifier))`.
2. Open in browser:
   `GET $ISSUER/connect/authorize?client_id=geek-seo-electron&response_type=code&scope=openid%20offline_access&redirect_uri=http://127.0.0.1/callback&code_challenge=CHALLENGE&code_challenge_method=S256`
3. Sign in at `/Account/Login` (and `/Account/TwoFactor` if enabled).
4. Approve consent if prompted; capture `code` from redirect.
5. Exchange:
   ```bash
   curl -sS -X POST "$ISSUER/connect/token" \
     -d "grant_type=authorization_code" \
     -d "client_id=geek-seo-electron" \
     -d "code=CODE_FROM_REDIRECT" \
     -d "redirect_uri=http://127.0.0.1/callback" \
     -d "code_verifier=YOUR_VERIFIER"
   ```

**Client credentials (machine client):**

```bash
curl -sS -X POST "$ISSUER/connect/token" \
  -d "grant_type=client_credentials" \
  -d "client_id=geekatyourspot-website" \
  -d "client_secret=$GEEK_WEBSITE_CLIENT_SECRET" \
  -d "scope=mcp.tools"
```

Run automated tests:

```bash
dotnet test GEEKBACKEND.slnx
TEST_DATABASE_URL="postgresql://..." dotnet test GEEKBACKEND.slnx
```

### appsettings.json Example

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "${DATABASE_URL}"
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "Console",
        "Args": { "theme": "Ansi" }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/geekbackend-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ]
  },
  "Jwt": {
    "Authority": "https://auth.geek.local",
    "Audience": "https://api.geek.local"
  }
}
```

## Security Notes

### Passwords

- **Algorithm:** BCrypt, work factor 12
- **Minimum:** 12 chars (1 upper, 1 lower, 1 digit, 1 symbol)
- **History:** Last 10 hashes stored (no reuse)
- **Validation:** Constant-time comparison (prevent timing attacks)

### Device Challenge-Response

- **Server:** Issues 60-second nonce
- **Client:** Signs nonce with device private key (RSA-2048)
- **Verification:** Compare against stored public key (immutable per device)
- **Use case:** Proves device possession; prevents device impersonation

### Tokens

- **Access token:** 15-minute TTL, JWT
- **Refresh token:** 30-day TTL, opaque, rotated on use
- **jti (JWT ID):** Unique per token; replay detection via blacklist (PostgreSQL now; Redis later)

### Audit Trail

- **Append-only:** audit_log table, immutable
- **Events:** Authentication, 2FA, authorization, device mgmt, token, password, administrative
- **Retention:** 12 months rolling; security_incidents preserved indefinitely
- **User-facing:** Users can view their own audit log via `/api/audit/me`

## Development

### Adding a New Repository

1. Create interface in `GeekApplication/Interfaces/I{Entity}Repository.cs`:
   ```csharp
   public interface IUserRepository
   {
       Task<Result<User>> FindByIdAsync(Guid userId);
       Task<Result<User>> FindByEmailAsync(string email);
       Task<Result<User>> CreateAsync(UserRequest request);
   }
   ```

2. Implement in `GeekRepository/Repositories/{Entity}Repository.cs` (Dapper for auth, EF for content)

3. Register in `GeekRepository/ServiceCollectionExtensions.cs`:
   ```csharp
   services.AddScoped<IUserRepository, UserRepository>();
   ```

4. Inject into service (not controller):
   ```csharp
   public class UserService(IUserRepository repo) { ... }
   ```

### Adding a New Entity

1. Define in `GeekApplication/Entities/{Entity}.cs`
2. If auth table: create Dapper repository
3. If content table: add DbSet to AppDbContext + migration
4. Add to Serilog audit schema if audit-relevant

### Running Migrations

```bash
# Create migration
dotnet ef migrations add AddUserTwoFactorSecret \
  --project GeekRepository \
  --startup-project GeekAPI \
  --output-dir Migrations

# Apply locally
dotnet ef database update --project GeekRepository --startup-project GeekAPI

# Generate SQL script (for production deploy)
dotnet ef migrations script > migrations.sql
```

## Testing

### Unit Tests (Repository Layer)

```csharp
[Fact]
public async Task CreateAsync_WithValidUser_ReturnsSuccess()
{
    var request = new CreateUserRequest { Email = "test@geek.local", Password = "..." };
    var result = await _userRepository.CreateAsync(request);
    
    Assert.True(result.IsSuccess);
    Assert.NotNull(result.Data);
}
```

### Integration Tests (API Layer)

```csharp
[Fact]
public async Task Post_Register_WithValidPayload_Returns201()
{
    var payload = new { email = "test@geek.local", password = "..." };
    var response = await _client.PostAsJsonAsync("/api/auth/register", payload);
    
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
}
```

### Security Test Checklist

- [ ] Passwords stored as BCrypt hashes (never plaintext)
- [ ] PKCE code_challenge validated before token exchange
- [ ] 2FA required for new devices (if enabled)
- [ ] Device fingerprint mismatch rejects authentication
- [ ] Rate limiting prevents brute force (login, token endpoint)
- [ ] Audit log captures all auth events
- [ ] Refresh token rotation invalidates old token
- [ ] jti blacklist prevents replay attacks
- [ ] WebSocket auth validates device fingerprint per message
- [ ] CORS restricts origin to approved domains only

## Deployment

### Railway

1. **Create PostgreSQL:** Railway Marketplace > PostgreSQL 16
2. **Create service:** Railway Marketplace > .NET Runtime
3. **Connect:** Link PostgreSQL to service; Railway auto-injects `DATABASE_URL`
4. **Deploy:** Push to `main` branch or use Railway CLI:
   ```bash
   railway deploy
   ```
5. **Verify:** Health check endpoint returns 200 OK:
   ```bash
   curl https://{service}.railway.app/health
   ```

### Local Docker

```bash
# Build image
docker build -t geekbackend:latest .

# Run with PostgreSQL
docker run --name geekbackend \
  -e DATABASE_URL="postgresql://..." \
  -p 5000:5000 \
  geekbackend:latest
```

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| `DbContext initialization failed` | Migration not applied | `dotnet ef database update` |
| `Authentication failed for user` | Wrong password hash algorithm | Verify BCrypt work factor = 12 |
| `Device fingerprint mismatch` | Device ID changed (OS reinstall) | Device re-registration flow |
| `TOTP code invalid` | Time skew >30 seconds | Sync system clock; increase TOTP_WINDOW_SIZE |
| `Rate limit exceeded (429)` | Too many failed login attempts | Wait 1 minute or request limit increase |
| `WebSocket connection refused` | Missing Bearer token | Include `Authorization: Bearer {token}` header |

## Documentation

- **[Architecture.md](./Architecture.md)** — 3-tier design, OAuth 2.1, TOTP, device mgmt, real-time sync
- **[COMPREHENSIVE_IMPLEMENTATION_PLAN.md](./GeekRepository/plan/COMPREHENSIVE_IMPLEMENTATION_PLAN.md)** — Authoritative specification (v3.4)
- **[CLAUDE.md](./CLAUDE.md)** — Development guidelines, architectural decisions, session notes

## License

Internal Geek At Your Spot project. Not for public distribution.

## Contact

Technical questions → `jmartinemployment@gmail.com`
