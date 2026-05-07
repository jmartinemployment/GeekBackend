# 3-Tier Architecture Refactoring Strategy

**Target**: Enforce clean separation using separate .NET projects with explicit dependency flow.

---

## Current State

```
GeekBackend.slnx
├── GeekBackend.Api/          (API + Services mixed together)
├── GeekBackend.Data/         (EF Core + Repositories)
```

---

## Proposed Multi-Project Structure

```
GeekBackend.slnx
├── GeekBackend.Core/         ✨ NEW — Domain contracts & entities (no dependencies)
│   ├── Entities/
│   ├── Repositories/         (interfaces only)
│   └── Services/             (interfaces only)
│
├── GeekBackend.Data/         ♻️ REFACTORED — Infrastructure/Persistence
│   ├── Data/
│   ├── Migrations/
│   ├── Repositories/         (implementations)
│   └── Configurations/
│
├── GeekBackend.Services/     ✨ NEW — Business logic layer
│   ├── Services/             (implementations)
│   ├── Dtos/                 (data transfer objects)
│   └── Mappers/
│
└── GeekBackend.Api/          ♻️ REFACTORED — Presentation layer
    ├── Controllers/
    ├── Middleware/
    ├── Program.cs
    └── Properties/
```

---

## Dependency Flow (Strict Unidirectional)

```
┌─────────────────────┐
│ GeekBackend.Api     │  Depends on: Core, Services
│ (Presentation)      │
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│ GeekBackend.Services│  Depends on: Core
│ (Business Logic)    │
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│ GeekBackend.Core    │  Depends on: NOTHING (pure contracts)
│ (Domain)            │
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│ GeekBackend.Data    │  Depends on: Core
│ (Infrastructure)    │
└─────────────────────┘
```

**Golden Rule**: No circular dependencies. Each layer only knows about layers below it.

---

## Phase 1: Create New Projects

### 1.1 GeekBackend.Core

**Purpose**: Pure domain contracts. Zero external dependencies.

```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

**Contents**:
- Move `GeekBackend.Data/Models/` → `GeekBackend.Core/Entities/`
- Move interface stubs (will create new ones):
  - `GeekBackend.Data/Repositories/I*.cs` → `GeekBackend.Core/Repositories/`
  - Service interfaces (to be created) → `GeekBackend.Core/Services/`

### 1.2 GeekBackend.Services

**Purpose**: Business logic orchestration. Depends only on Core.

```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\GeekBackend.Core\GeekBackend.Core.csproj" />
  </ItemGroup>
</Project>
```

**Contents**:
- Move `GeekBackend.Api/Services/*.cs` → `GeekBackend.Services/Services/`
- Move `GeekBackend.Api/Dtos/*.cs` → `GeekBackend.Services/Dtos/`
- Add `GeekBackend.Services/Mappers/` for Entity→DTO conversions

---

## Phase 2: Update GeekBackend.Data

### 2.1 Project File Updates

```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\GeekBackend.Core\GeekBackend.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- EF Core packages stay the same -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.7">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <!-- ... rest of packages -->
  </ItemGroup>
</Project>
```

### 2.2 Folder Reorganization

- Delete `GeekBackend.Data/Class1.cs` (temp file)
- Keep `Repositories/` → contains implementations only (interfaces now in Core)
- Keep `Data/AppDbContext.cs`
- Keep `Migrations/`
- Move database configuration to `Configurations/`

### 2.3 Repository Interface Cleanup

Current repositories live in `GeekBackend.Data/Repositories/I*.cs`.
- Move interfaces → `GeekBackend.Core/Repositories/`
- Keep implementations → `GeekBackend.Data/Repositories/`

---

## Phase 3: Refactor GeekBackend.Api

### 3.1 Project File Updates

```csproj
<Project Sdk="Microsoft.NET.Sdk.Web">
  <!-- ... properties ... -->

  <ItemGroup>
    <ProjectReference Include="..\GeekBackend.Core\GeekBackend.Core.csproj" />
    <ProjectReference Include="..\GeekBackend.Services\GeekBackend.Services.csproj" />
    <ProjectReference Include="..\GeekBackend.Data\GeekBackend.Data.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- HTTP/Web-specific packages -->
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.7" />
    <!-- ... -->
  </ItemGroup>
</Project>
```

### 3.2 Controller Refactoring

**BEFORE**:
```csharp
public DepartmentsController(
    IDepartmentRepository departments,           // ❌ Direct repo
    IUseCaseRepository useCases,                 // ❌ Direct repo
    DepartmentContentService departmentService  // ✓ Service
)
```

**AFTER**:
```csharp
public DepartmentsController(
    IDepartmentService departmentService        // ✓ ONLY services
)
```

Service layer now orchestrates all data access.

### 3.3 Folder Cleanup

- Delete `GeekBackend.Api/Services/` (moved to GeekBackend.Services)
- Delete `GeekBackend.Api/Dtos/` (moved to GeekBackend.Services)
- Keep only: Controllers, Middleware, Program.cs, Properties

---

## Phase 4: Solution File (.slnx) Update

```xml
<Solution>
  <Project Path="GeekBackend.Core/GeekBackend.Core.csproj" />
  <Project Path="GeekBackend.Data/GeekBackend.Data.csproj" />
  <Project Path="GeekBackend.Services/GeekBackend.Services.csproj" />
  <Project Path="GeekBackend.Api/GeekBackend.Api.csproj" />
</Solution>
```

**Order matters** (for readability, reflect dependency order).

---

## Migration Path (Execution Sequence)

### Step 1: Create New Projects (no code changes yet)
- [ ] Create `GeekBackend.Core.csproj`
- [ ] Create `GeekBackend.Services.csproj`
- [ ] Update `.slnx`

### Step 2: Move Contracts to Core
- [ ] Move Models → Core/Entities
- [ ] Move Repository interfaces → Core/Repositories
- [ ] Create Service interfaces → Core/Services

### Step 3: Move Data Access Infrastructure
- [ ] Keep Repository implementations in Data
- [ ] Add Core reference to Data.csproj

### Step 4: Move Business Logic to Services
- [ ] Move Services → Services project
- [ ] Move DTOs → Services project
- [ ] Add mappings: Entities → DTOs
- [ ] Add Core reference to Services.csproj

### Step 5: Refactor Controllers
- [ ] Remove direct repository injections
- [ ] Keep only service injections
- [ ] Update namespace imports

### Step 6: Update Dependency Injection (Program.cs)
- [ ] Register Core services
- [ ] Register Services implementations
- [ ] Register Data repositories
- [ ] Verify no circular references

---

## Namespace Convention

| Project | Namespace | Example |
|---------|-----------|---------|
| Core | `GeekBackend.Core` | `GeekBackend.Core.Entities.Department` |
| Core | (Interfaces) | `GeekBackend.Core.Repositories.IDepartmentRepository` |
| Data | `GeekBackend.Data` | `GeekBackend.Data.Repositories.DepartmentRepository` |
| Services | `GeekBackend.Services` | `GeekBackend.Services.Services.DepartmentService` |
| Services | (DTOs) | `GeekBackend.Services.Dtos.DepartmentDto` |
| Api | `GeekBackend.Api` | `GeekBackend.Api.Controllers.DepartmentsController` |

---

## Key Benefits of This Structure

✅ **Enforced Layering**: Compiler prevents cross-layer references  
✅ **Independent Deployment**: Services layer can be versioned separately  
✅ **Testability**: Easy to mock Core interfaces in Data/Services tests  
✅ **Scalability**: Core stays small, focused, and stable  
✅ **Maintainability**: Clear boundaries reduce cognitive load  
✅ **.NET 10 Ready**: Uses modern async/await patterns throughout  

---

## Potential Challenges & Mitigations

| Challenge | Mitigation |
|-----------|-----------|
| **Build times increase** | Projects are small; modern .NET is fast |
| **Cross-project navigation in IDE** | IDE support (F12 goto) handles this seamlessly |
| **Shared exceptions/utilities** | Create `GeekBackend.Shared/` for common types (logging, exceptions) |
| **EF Migrations** | Migrations stay in Data project; run `dotnet ef` from Data folder |

---

## Next Steps

1. **Review & Approve** this strategy
2. **Create new projects** (Core, Services)
3. **Execute migration** in phases (contracts → logic → controllers)
4. **Run tests** at each phase (ensures no breakage)
5. **Update CI/CD** if applicable

---

## Questions to Clarify Before Execution

- [ ] Should we create a `GeekBackend.Shared/` project for common types (Result, Exceptions)?
- [ ] Do you want separate test projects for each layer, or one centralized test project?
- [ ] Any authentication/authorization logic that needs special handling?
- [ ] Should DTOs live in Services or in a separate `GeekBackend.Contracts/` project?
