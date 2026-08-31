# DhirDhar Solution

Windows desktop application for managing DhirDhar operations.

## Current Phase

**Phase 2 — Local Database & Data Persistence**

## Technology

- **.NET 8**
- **C#**
- **WinUI 3** (Windows App SDK)
- **SQLite** with **Entity Framework Core** (EF Core migrations)
- **x64** Windows desktop
- **Clean Architecture**

## Architecture

Clean Architecture with four projects:

| Project | Responsibility |
| --- | --- |
| `DhirDhar.Domain` | Core business entities, value objects, domain exceptions. No dependencies. |
| `DhirDhar.Application` | Application services, abstractions, results, exceptions. Depends only on Domain. |
| `DhirDhar.Infrastructure` | Persistence, configuration, logging, storage implementations. Depends on Application and Domain. |
| `DhirDhar.Desktop` | WinUI 3 user interface, composition root, startup pipeline. Depends on Application and Infrastructure. |

Dependency direction:

```
Domain  →  Application  →  Infrastructure  →  Desktop
```

## Current Scope

This phase provides the application foundation and local persistence:

- Clean Architecture solution structure
- Dependency Injection composition root (Microsoft.Extensions.DependencyInjection)
- Centralized configuration (`appsettings.json` + typed options)
- Logging foundation (Debug + rolling file logger)
- Global exception handling hooks
- Centralized application metadata
- Startup pipeline (Configuration → Logging → DI → Database Initialization → Database Health Check → Main Window)
- Minimal functional `MainWindow`
- Domain foundation: `Entity`, `ValueObject`, `DomainException`
- Application foundation: `Result`, `Result<T>`, application exceptions, abstractions
- Infrastructure foundation: options, path service, time service, file logging
- **SQLite database** stored in `%LOCALAPPDATA%\DhirDhar Solution\Data\DhirDhar.db`
- **EF Core migrations** (initial migration `InitialCreate`; no production tables yet)
- `DhirDharDbContext` + design-time `DbContextFactory` + shared options wiring
- `DatabaseInitializer` (full startup pipeline: resolve, verify, open, check migrations, apply, confirm)
- `DatabaseHealthService` (path, connection, migration status, basic read — structured result)
- Generic repository infrastructure and `IUnitOfWork` (transaction commit/rollback)
- Backup abstraction (`IDatabaseBackupSource`) for a later phase
- Unit test projects for Domain, Application, and Infrastructure

## Not Yet Implemented

Financial functionality is intentionally **not** implemented in this phase:

- Borrower, loan, deposit, withdrawal, interest, and ledger management
- Dashboard and reports
- Google Drive / cloud backup
- User authentication and accounts
- Android application

These are planned for later phases.

## Getting Started

```powershell
dotnet restore "DhirDhar Solution.slnx"
dotnet build "DhirDhar Solution.slnx" -c Debug
dotnet test "DhirDhar Solution.slnx" -c Debug
```

Run the desktop application:

```powershell
dotnet run --project src/DhirDhar.Desktop -c Debug
```

See [docs/architecture/README.md](docs/architecture/README.md) for architecture details.
