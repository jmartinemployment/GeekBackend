# GeekBackend

.NET 10 ASP.NET Core Web API backend for the OrderStack restaurant management SaaS platform. Connected to a shared Supabase PostgreSQL instance.

## Tech Stack

- **Runtime:** .NET 10 / ASP.NET Core
- **ORM:** Entity Framework Core 10 + Npgsql
- **Database:** PostgreSQL via Supabase
- **Architecture:** DB-first scaffolded models + code-first migrations for new features

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Access to the Supabase project (`mpnruwauxsqbrxvlksnf`)

### Configuration

Create a `.env` file at the solution root:

```
DATABASE_URL="postgresql://postgres.[project-ref]:[password]@aws-1-us-east-1.pooler.supabase.com:6543/postgres?pgbouncer=true"
DIRECT_URL="postgresql://postgres:[password]@db.[project-ref].supabase.co:5432/postgres"
```

`DATABASE_URL` is used at runtime (PgBouncer pooler). `DIRECT_URL` is used for migrations (direct connection required by Supabase).

### Run

```bash
dotnet run --project GeekBackend.Api
# API available at http://localhost:5272
```

### Apply Migrations

```bash
dotnet ef database update --project GeekBackend.Data --startup-project GeekBackend.Api
```

## Project Structure

```
GeekBackend.Api/        # ASP.NET Core Web API — routes, controllers, DI setup
GeekBackend.Data/
  Data/                 # AppDbContext (scaffolded) + Extensions partial class
  Models/               # 182 scaffolded models + 6 code-first entity classes
  Migrations/           # EF migrations (code-first tables only)
  Repositories/         # Repository interfaces and implementations
```

## Data Layer Notes

The `AppDbContext` is scaffolded DB-first from the existing Supabase schema. To re-scaffold after a schema change in Supabase:

```bash
cd GeekBackend.Data
dotnet ef dbcontext scaffold "CONNECTION_STRING" Npgsql.EntityFrameworkCore.PostgreSQL \
  --output-dir Models --context-dir Data --context AppDbContext \
  --namespace GeekBackend.Data.Models --context-namespace GeekBackend.Data.Data --force
```

New feature tables (not in Supabase yet) use code-first migrations. The `AppDbContextModelSnapshot` only tracks migration-managed tables — the scaffolded tables are excluded intentionally.
