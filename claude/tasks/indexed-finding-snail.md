# GeekBackend Architecture Refactor Plan

## Context

Previous Claude session scaffolded the entire wrong database (Supabase `auth.*` + OrderStack
restaurant schema) into `GeekBackend.Data/Models/`, creating ~150 model files that don't belong
here. It then wired repositories and controllers against those wrong models, bypassed the
planned 4-tier architecture, used EF Core where Dapper was specified, and stored passwords in
plaintext. The `COMPREHENSIVE_IMPLEMENTATION_PLAN.md` describes the correct architecture.

---

## Target Architecture

```
Geek.slnx
├── GeekShared/       ← Domain contracts — entities, interfaces, Result<T> (no DB deps)
├── GeekRepository/   ← Persistence — Dapper (auth) + EF Core (content)
├── GeekServices/     ← Business logic — services call repos
└── GeekAPI/          ← HTTP — controllers call services only
```

**Data access split:**
- EF Core → content tables only (`departments`, `case_studies`, `use_cases`)
- Dapper → auth tables (`users`, `devices`, `oauth_*`, `oidc_*`, `audit_log`, `rbac`)

---

## Problems to Fix

| # | Problem | Severity |
|---|---------|----------|
| 1 | `UserAuthRepository.CreateAsync` sets `user.Password = request.Password` (plaintext) | **CRITICAL** |
| 2 | `User.cs` is Supabase's `auth.users` schema, not a GeekBackend entity | High |
| 3 | ~150 scaffolded models from wrong databases (Supabase internal + OrderStack) | High |
| 4 | No `GeekShared` project — interfaces mixed into Data | High |
| 5 | No `GeekServices` project — controllers call repos directly | High |
| 6 | Auth repos use EF Core — plan specifies Dapper | Medium |
| 7 | `AppDbContext.Extensions.cs` maps auth `DbSet`s against wrong models | Medium |
| 8 | Solution has 2 projects with wrong names; target requires 4 with Geek* names | Medium |

---

## Execution Plan

### Step 0 — Rename existing projects to Geek* naming

**This step runs first. All subsequent steps use the new names.**

#### 0a. Rename GeekBackend.Api → GeekAPI

Shell operations:
```bash
mv GeekBackend.Api GeekAPI
mv GeekAPI/GeekBackend.Api.csproj GeekAPI/GeekAPI.csproj
```

Namespace updates — replace `GeekBackend.Api` with `GeekAPI` in every .cs file under `GeekAPI/`:

| File | Old namespace | New namespace |
|------|--------------|--------------|
| Controllers/Auth/AuditController.cs | `GeekBackend.Api.Controllers.Auth` | `GeekAPI.Controllers.Auth` |
| Controllers/Auth/AuthTokensController.cs | `GeekBackend.Api.Controllers.Auth` | `GeekAPI.Controllers.Auth` |
| Controllers/Auth/DevicesController.cs | `GeekBackend.Api.Controllers.Auth` | `GeekAPI.Controllers.Auth` |
| Controllers/Auth/OAuthClientsController.cs | `GeekBackend.Api.Controllers.Auth` | `GeekAPI.Controllers.Auth` |
| Controllers/Auth/OidcStorageController.cs | `GeekBackend.Api.Controllers.Auth` | `GeekAPI.Controllers.Auth` |
| Controllers/Auth/PendingVerificationsController.cs | `GeekBackend.Api.Controllers.Auth` | `GeekAPI.Controllers.Auth` |
| Controllers/Auth/RbacController.cs | `GeekBackend.Api.Controllers.Auth` | `GeekAPI.Controllers.Auth` |
| Controllers/Auth/UsersController.cs | `GeekBackend.Api.Controllers.Auth` | `GeekAPI.Controllers.Auth` |
| Controllers/CaseStudiesController.cs | `GeekBackend.Api.Controllers` | `GeekAPI.Controllers` |
| Controllers/DepartmentsController.cs | `GeekBackend.Api.Controllers` | `GeekAPI.Controllers` |
| Controllers/UseCasesController.cs | `GeekBackend.Api.Controllers` | `GeekAPI.Controllers` |
| Dtos/CaseStudyDtos.cs | `GeekBackend.Api.Dtos` | `GeekAPI.Dtos` |
| Dtos/DepartmentDto.cs | `GeekBackend.Api.Dtos` | `GeekAPI.Dtos` |
| Dtos/RequestDtos.cs | `GeekBackend.Api.Dtos` | `GeekAPI.Dtos` |
| Dtos/UseCaseDto.cs | `GeekBackend.Api.Dtos` | `GeekAPI.Dtos` |
| Middleware/ApiKeyMiddleware.cs | `GeekBackend.Api.Middleware` | `GeekAPI.Middleware` |
| Services/DepartmentContentService.cs | `GeekBackend.Api.Services` | `GeekAPI.Services` |

`Program.cs` uses top-level statements (no namespace declaration) — update only `using` imports.

Update `GeekAPI/GeekAPI.csproj` project reference:
```xml
<!-- old --> <ProjectReference Include="..\GeekBackend.Data\GeekBackend.Data.csproj" />
<!-- new --> <ProjectReference Include="..\GeekRepository\GeekRepository.csproj" />
```

#### 0b. Rename GeekBackend.Data → GeekRepository

Shell operations:
```bash
mv GeekBackend.Data GeekRepository
mv GeekRepository/GeekBackend.Data.csproj GeekRepository/GeekRepository.csproj
```

Namespace replacements — `GeekBackend.Data` → `GeekRepository` everywhere in `GeekRepository/`:

| Old namespace root | New namespace root | Affected files |
|--------------------|-------------------|----------------|
| `GeekBackend.Data` | `GeekRepository` | Class1.cs, GlobalUsings.cs |
| `GeekBackend.Data.Data` | `GeekRepository.Data` | AppDbContext.cs, AppDbContext.Extensions.cs, AppDbContextDesignTimeFactory.cs |
| `GeekBackend.Data.Migrations` | `GeekRepository.Migrations` | 5 migration files |
| `GeekBackend.Data.Models` | `GeekRepository.Models` | ~160 model files |
| `GeekBackend.Data.Repositories` | `GeekRepository.Repositories` | 22 repository files |
| `GeekBackend.Data.Results` | `GeekRepository.Results` | Result.cs |

Also update all `using GeekBackend.Data.*` imports in `GeekAPI/` source files.

#### 0c. Rename solution file

```bash
mv GeekBackend.slnx Geek.slnx
```

Update `Geek.slnx` content:
```xml
<Solution>
  <Project Path="GeekAPI/GeekAPI.csproj" />
  <Project Path="GeekRepository/GeekRepository.csproj" />
</Solution>
```

#### 0d. Verify build after rename
```bash
dotnet build Geek.slnx
```
Expected: same result as before rename — compile errors from wrong architecture remain,
but no new "project not found" or "namespace not found" errors.

---

### Step 1 — Purge scaffolded model garbage

**Files to delete:** All files in `GeekRepository/Models/` **except** these 6 content models:
- `Department.cs`, `CaseStudy.cs`, `CaseStudyMetric.cs`, `CaseStudyActor.cs`,
  `CaseStudyEventFlowStep.cs`, `UseCase.cs`

Everything else goes: the ~140 Supabase/OrderStack scaffolded files including
`User.cs`, `User1.cs`, `User2.cs`, `Device.cs`, `Device1.cs`, `OauthToken.cs`, etc.

**Risk:** Verify content models have no `partial` or nav-property references into deleted models.

---

### Step 2 — Strip auth DbSets from AppDbContext.Extensions.cs

**File:** `GeekRepository/Data/AppDbContext.Extensions.cs`

Remove all `DbSet<>` properties and `modelBuilder` config blocks for auth/audit entities:
`Users`, `Roles`, `Permissions`, `UserRoles`, `PendingVerifications`, `DevicesAuth`,
`OAuthTokens`, `OAuthClients`, `OidcStorages`, `AuditLogs`, `CircuitResets`.

Keep only the 6 content `DbSet`s and their `OnModelCreatingPartial` blocks.

**Dependency:** Step 1 must complete first.

---

### Step 3 — Create GeekShared project

**New project:** `GeekShared/GeekShared.csproj`
- `TargetFramework net10.0`, no DB package dependencies

**New files — Domain entities** (matching `PostgreSchema.txt`):
- `GeekShared/Entities/User.cs` — `Id` (Guid), `Subject`, `Username`, `Email`,
  `PasswordHash`, `TwoFactorEnabled`, `TwoFactorSecret`, `CreatedAt`
- `GeekShared/Entities/Device.cs` — `Id`, `UserId`, `DeviceType`, `DeviceName`,
  `BiosId`, `Platform`, `LastSeenAt`, `IsTrusted`
- `GeekShared/Entities/OidcApplication.cs`, `OidcToken.cs`, `OidcAuthorization.cs`
- `GeekShared/Entities/AuditLog.cs`, `Role.cs`, `Permission.cs`

**New files — Repository interfaces** (move from `GeekRepository/Repositories/`):
- `GeekShared/Repositories/IUserAuthRepository.cs`
- `GeekShared/Repositories/IDeviceRepository.cs`
- `GeekShared/Repositories/IOAuthTokenRepository.cs`
- `GeekShared/Repositories/IOAuthClientRepository.cs`
- `GeekShared/Repositories/IOidcStorageRepository.cs`
- `GeekShared/Repositories/IPendingVerificationRepository.cs`
- `GeekShared/Repositories/IAuditRepository.cs`
- `GeekShared/Repositories/IRbacRepository.cs`

**Move** `GeekRepository/Results/Result.cs` → `GeekShared/Results/Result.cs`

**Add to solution:** `Geek.slnx`

---

### Step 4 — Restructure GeekRepository

**Changes to `GeekRepository.csproj`:**
- Add `<ProjectReference Include="..\GeekShared\GeekShared.csproj" />`
- Add `BCrypt.Net-Next` package (version 4.x)

**Delete** the old interface files from `GeekRepository/Repositories/`:
`IUserAuthRepository.cs`, `IDeviceRepository.cs`, `IOAuthTokenRepository.cs`,
`IOAuthClientRepository.cs`, `IOidcStorageRepository.cs`, `IPendingVerificationRepository.cs`,
`IAuditRepository.cs`, `IRbacRepository.cs`

**Update all auth repository implementations** to:
- Reference interfaces from `GeekShared.Repositories`
- Reference entities from `GeekShared.Entities`
- Reference `Result<T>` from `GeekShared.Results`
- Remove EF Core — use Dapper with `IDbConnection`
- **`UserAuthRepository.CreateAsync`**: hash with `BCrypt.Net.BCrypt.HashPassword()`,
  store in `password_hash` column. Remove plaintext `Password` field entirely.

**Content repos** (`DepartmentRepository`, `CaseStudyRepository`, `UseCaseRepository`) —
keep EF Core, update `using` for `Result<T>` new location.

---

### Step 5 — Create GeekServices project

**New project:** `GeekServices/GeekServices.csproj`
- References: `GeekShared`

**New files — Service interfaces** in `GeekShared/Services/`:
- `IUserService.cs` — `RegisterAsync`, `FindByIdAsync`, `FindByEmailAsync`, `UpdateAsync`, `DeleteAsync`
- `IDeviceService.cs` — `RegisterDeviceAsync`, `TrustDeviceAsync`, `GetUserDevicesAsync`

**New files — Implementations** in `GeekServices/`:
- `UserService.cs` — orchestrates `IUserAuthRepository`; owns business rules (duplicate email check)
- `DeviceService.cs` — orchestrates `IDeviceRepository`

**Add to solution:** `Geek.slnx`

---

### Step 6 — Update GeekAPI

**Changes to `GeekAPI.csproj`:**
- Remove direct `GeekRepository` reference (API must not know about Dapper/EF)
- Add `<ProjectReference Include="..\GeekServices\GeekServices.csproj" />`
- Add `<ProjectReference Include="..\GeekShared\GeekShared.csproj" />` (for DTOs/interfaces)

**Changes to `Program.cs`:**
- Remove `AddScoped` for auth repositories
- Add `AddScoped<IUserService, UserService>()`, `AddScoped<IDeviceService, DeviceService>()`
- Add `builder.Services.AddGeekData(connectionString)` (extension in GeekRepository that
  registers all repos + DbContext)

**Changes to `Controllers/Auth/UsersController.cs`:**
- Constructor takes `IUserService`, not `IUserAuthRepository`
- Same pattern for all other auth controllers

**`ApiKeyMiddleware.cs`** — keep as-is; OpenIddict replaces it in the next phase.

---

### Step 7 — Verify build

```bash
dotnet build Geek.slnx
```
Expected: 0 errors, 0 warnings about missing types.

---

## Files Modified Summary

| Action | File/Directory |
|--------|---------------|
| RENAME folder | `GeekBackend.Api/` → `GeekAPI/` |
| RENAME folder | `GeekBackend.Data/` → `GeekRepository/` |
| RENAME file | `GeekBackend.slnx` → `Geek.slnx` |
| RENAME + update | `GeekAPI.csproj` (17 namespace updates, 1 project ref update) |
| RENAME + update | `GeekRepository.csproj` (~193 namespace updates) |
| UPDATE | `Geek.slnx` (new project paths) |
| DELETE ~140 files | `GeekRepository/Models/` (all except 6 content models) |
| MODIFY | `GeekRepository/Data/AppDbContext.Extensions.cs` |
| MODIFY | `GeekRepository/GeekRepository.csproj` |
| MODIFY | `GeekRepository/Repositories/*.cs` (all implementations) |
| DELETE 8 files | `GeekRepository/Repositories/I*.cs` (moving to GeekShared) |
| MOVE | `GeekRepository/Results/Result.cs` → `GeekShared/Results/Result.cs` |
| CREATE | `GeekShared/` (new project, entities, interfaces, Result) |
| CREATE | `GeekServices/` (new project, service implementations) |
| MODIFY | `GeekAPI/Program.cs` |
| MODIFY | `GeekAPI/Controllers/Auth/*.cs` (all 8 controllers) |

---

## What This Plan Does NOT Change

- Existing content API (departments, case studies, use cases) — keep working
- `ApiKeyMiddleware` — not touched until OpenIddict phase
- `GeekRepository/PostgreSchema.txt` — already correct PostgreSQL DDL
- `COMPREHENSIVE_IMPLEMENTATION_PLAN.md` — this refactor implements Phase 1 of that plan

---

## Security Note

Step 4 fixes plaintext password storage before any other code is written.
Non-negotiable — executes as part of this refactor, not deferred.
