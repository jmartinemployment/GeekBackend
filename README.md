# GeekBackend

Platform backend for Geek applications: auth storage APIs, TOTP 2FA, multi-device management, real-time session sync, and public content APIs.

**Status:** GeekAPI is the platform gateway (no authorization server in this repo). GeekRepository holds PostgreSQL data (Dapper for auth tables, EF Core for content). Authorization server is a separate .NET deployment at `auth.geekatyourspot.com` (planned).

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

Layer boundaries enforced at `.csproj` reference level. See [Architecture.md](./Architecture.md) for platform topology, S2S auth target (JWT, not shared repo keys), and deployment rules.

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
    │   ├── Dapper/             # Atomic auth operations (users, devices, oidc storage, audit, rbac)
    │   └── EF/                 # Relational content (departments, case_studies, use_cases)
    └── PostgreSchema.txt       # DDL for all tables
```

## Implementation Status

| Component | Status | Owner | ETA |
|-----------|--------|-------|-----|
| **Entities & Repos** | ✅ Complete | — | — |
| **AppDbContext + Migrations** | ✅ Complete | — | — |
| **DeviceOauth fingerprint** | ✅ Complete | — | — |
| **Legacy `/api/auth/*` + SyncHub** | ❌ Retired (410) | — | Use **GeekOAuth**; removed from code May 2026 |
| **Geek SEO internal proxy** | ✅ Implemented | GeekAPI | `/api/seo/internal/*` |
| **Security hardening** | ✅ Implemented | GeekAPI | Headers, CORS, API key |
| **Platform authorization server** | ✅ External | **GeekOAuth** | `auth.geekatyourspot.com` |

## Key Concepts

### Data access (current)

**EF Core — product SEO (`geek_seo`):** Owned by **GeekSeo.Persistence** in the Geek-SEO repo; applied at GeekRepository startup via `SeoDbContext`.

**EF Core — platform content:** `departments`, `case_studies`, `use_cases` via `AppDbContext`.

**Legacy Dapper auth tables:** Dropped by SQL migration `0006_drop_legacy_platform_auth.sql` (May 2026). Login and devices live in **GeekOAuth** (`geek_oauth` / `asp_net_users`), not this Postgres instance.

### Identity

Use **GeekOAuth** for OIDC/OAuth 2.1, PKCE, and JWT issuance. GeekAPI and GeekRepository expose **410 Gone** on retired `/api/auth/*` and `/repo/auth/*` paths.

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

# GeekAPI → GeekRepository
REPO_URL="https://your-geekrepository.up.railway.app"
# REPO_API_KEY — local/CI interim only; production uses geekapi client_credentials JWT (see Architecture.md)
GEEK_BACKEND_API_KEY="api-key-for-inbound-calls"
CORS_ORIGINS="https://www.geekatyourspot.com,http://127.0.0.1"

# Integration tests (optional)
TEST_DATABASE_URL="postgresql://..."
TEST_REPO_URL="http://127.0.0.1:5050"
```

## Tests

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

## Security (current)

- **Identity:** GeekOAuth — passwords, TOTP, and tokens are not stored in GeekBackend Postgres.
- **GeekAPI / GeekRepository:** `X-API-Key` / `REPO_API_KEY` for machine access; SEO internal proxy requires user id headers from GeekSeoBackend.
- **Legacy paths:** `/api/auth/*`, `/repo/auth/*` return **410 Gone**.

## Development

### SEO schema (`geek_seo`)

Owned by **GeekSeo.Persistence** in the Geek-SEO repo:

```bash
cd Geek-SEO
dotnet ef migrations add MigrationName --project GeekSeo.Persistence
dotnet ef database update --project GeekSeo.Persistence
```

GeekRepository applies EF migrations at startup; SQL files in `GeekRepository/Migrations/Sql/` run via `SqlMigrationRunner`.

### Platform content (`AppDbContext`)

```bash
cd GeekBackend
dotnet ef migrations add MigrationName --project GeekRepository --startup-project GeekRepository
dotnet ef database update --project GeekRepository --startup-project GeekRepository
```

### Adding a SEO internal route

1. Controller under `GeekRepository/Controllers/Seo/` with route `repo/seo/...`
2. GeekAPI exposes it automatically via `SeoInternalProxyController` at `/api/seo/internal/...`
3. GeekSeoBackend calls the proxy via `GeekDataGateway` HTTP client

## Testing

```bash
cd GeekBackend
dotnet build GEEKBACKEND.slnx
dotnet test GEEKBACKEND.slnx
```

Auth integration tests were removed with M4–M6. Add new tests against content or SEO internal routes as needed.

## Deployment

### Railway

**Two Railway projects** from this repo (do not share one root `railway.toml`):

| Service | Dockerfile | Config-as-code |
|---------|------------|----------------|
| **GeekAPI** | `./Dockerfile` | none (default) |
| **GeekRepository** | `./Dockerfile.repository` | `railway.geekrepository.toml` (set path in service settings) |

GeekRepository clones **Geek-SEO** at the SHA in `Geek-SEO.commit`.

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

- **[Architecture.md](./Architecture.md)** — historical 3-tier design notes (issuer sections superseded)
- **[COMPREHENSIVE_IMPLEMENTATION_PLAN.md](./GeekRepository/plan/COMPREHENSIVE_IMPLEMENTATION_PLAN.md)** — Authoritative specification (v3.4)
- **[CONTRIBUTING.md](./CONTRIBUTING.md)** — Commit standards (no AI attribution), local tooling rules
- **[GeekAPI/README.md](./GeekAPI/README.md)** — GeekAPI routes and environment

## License

Internal Geek At Your Spot project. Not for public distribution.

## Contact

Technical questions → `jmartinemployment@gmail.com`
