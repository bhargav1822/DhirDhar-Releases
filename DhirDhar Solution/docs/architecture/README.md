# DhirDhar Solution — Architecture

This document describes the architecture implemented in Phases 1 and 2 and is kept synchronized with the code.

## Solution Structure

```
DhirDhar Solution/
├── DhirDhar Solution.slnx
├── src/
│   ├── DhirDhar.Desktop/        # WinUI 3 application (UI + composition root)
│   ├── DhirDhar.Application/    # Application use cases and abstractions
│   ├── DhirDhar.Domain/         # Core domain model
│   └── DhirDhar.Infrastructure/ # Persistence, logging, storage, options
├── tests/
│   ├── DhirDhar.Domain.Tests/
│   ├── DhirDhar.Application.Tests/
│   └── DhirDhar.Infrastructure.Tests/
└── docs/architecture/
```

## Project Responsibilities

- **DhirDhar.Domain** — Entities, value objects, domain exceptions, domain rules. Contains no framework references and no dependency on any other project in the solution.
- **DhirDhar.Application** — Orchestrates use cases, defines abstractions (persistence, services), results, and application exceptions. Depends only on the domain layer.
- **DhirDhar.Infrastructure** — Implements the abstractions declared by the application layer: database context and migrations, database initialization and health, path resolution, generic repositories, unit of work, time services, file logging, and typed configuration options. Depends on the application and domain layers.
- **DhirDhar.Desktop** — WinUI 3 user interface. Contains the composition root that composes all services and the startup pipeline. Depends on the application and infrastructure layers.

## Dependency Direction

```
DhirDhar.Domain
      ↓
DhirDhar.Application
      ↓
DhirDhar.Infrastructure
      ↓
DhirDhar.Desktop
```

References flow downward only; there are no circular references. Tests may reference the project they test and required lower-level dependencies.

## Dependency Injection

`Microsoft.Extensions.DependencyInjection` is used.

- **DhirDhar.Application** — `ApplicationServiceRegistration.AddApplication(IServiceCollection)`
- **DhirDhar.Infrastructure** — `InfrastructureServiceRegistration.AddInfrastructure(IServiceCollection, IConfiguration)`
- **DhirDhar.Desktop** — `DesktopServiceRegistration.AddDesktop(IServiceCollection)` and `ConfigurationExtensions.AddDesktopServices(IServiceCollection, IConfiguration)` which composes all layers.

The **composition root lives in the Desktop project** (`App.xaml.cs`). Services are resolved from the container; the service-locator pattern is avoided.

## Configuration

Centralized configuration is loaded from `appsettings.json` using `Microsoft.Extensions.Configuration`. Typed options are bound per section:

| Section | Options type | Location |
| --- | --- | --- |
| `Application` | `AppOptions` | `DhirDhar.Desktop/Configuration` |
| `Database` | `DatabaseOptions` | `DhirDhar.Infrastructure/Configuration` |
| `Backup` | `BackupOptions` | `DhirDhar.Infrastructure/Configuration` |
| `Security` | `SecurityOptions` | `DhirDhar.Infrastructure/Configuration` |
| `Logging` | `LoggingOptions` | `DhirDhar.Infrastructure/Configuration` |
| `Localization` | `LocalizationOptions` | `DhirDhar.Infrastructure/Configuration` |

Application metadata (name, version, environment) is centralized in the `Application` section and consumed from `AppOptions`; the UI reads the version from there rather than duplicating it.

## Logging

Logging uses the `Microsoft.Extensions.Logging` abstraction. The pipeline is configured in `DesktopLoggingExtensions`:

- `Debug` provider
- Rolling daily `FileLoggerProvider` writing to `%LOCALAPPDATA%\DhirDhar Solution\Logs`

The log directory is resolved centrally by `IDatabasePathService` so path logic is not duplicated.

Minimum level is read from the `Logging:MinimumLevel` section so Development and Production can differ. Logging never includes sensitive financial information, passwords, authentication secrets, or Google credentials.

## Startup Lifecycle

The startup pipeline is orchestrated by `IApplicationStartupService` / `ApplicationStartupService` in the Desktop project:

1. Application launch (`App.OnLaunched`)
2. Configuration initialization (build `IConfiguration`, bind `AppOptions`)
3. Logging initialization
4. Dependency Injection initialization (build `IServiceProvider`)
5. Database initialization (`IDatabaseInitializer.InitializeAsync` in a short-lived scope)
6. Database health check (`IDatabaseHealthService.CheckAsync` in a short-lived scope)
7. Main Window created and activated

The design supports reporting each stage on a future loading screen; no artificial delays are used.

## Database & Persistence

SQLite is used through Entity Framework Core. The database is stored in the user's local application data directory (never beside the executable):

```
%LOCALAPPDATA%\DhirDhar Solution\Data\DhirDhar.db
```

`DatabasePathService` (`DhirDhar.Infrastructure/Persistence`) centrally resolves the application data directory, database directory, database file, backup directory, and log directory. No username, drive letter, or absolute user path is hardcoded.

| Piece | Type | Location |
| --- | --- | --- |
| Database context | `DhirDharDbContext` | `DhirDhar.Infrastructure/Persistence` |
| Design-time factory | `DbContextFactory` (`IDesignTimeDbContextFactory`) | `DhirDhar.Infrastructure/Persistence` |
| Options wiring | `DbContextOptionsFactory` | `DhirDhar.Infrastructure/Persistence` |
| Initializer | `DatabaseInitializer` | `DhirDhar.Infrastructure/Persistence` |
| Health check | `DatabaseHealthService` | `DhirDhar.Infrastructure/Persistence` |
| Repository | `Repository<TEntity>` | `DhirDhar.Infrastructure/Persistence/Repositories` |
| Unit of work | `UnitOfWork` | `DhirDhar.Infrastructure/Persistence/Repositories` |
| Migrations | `InitialCreate` | `DhirDhar.Infrastructure/Persistence/Migrations` |

Abstractions live in `DhirDhar.Application/Abstractions/Persistence` (plus `Repositories` and `Backup`): `IDatabaseInitializer`, `IDatabaseHealthService`, `IDatabasePathService`, `IRepository<TEntity>`, `IUnitOfWork`, and the architectural `IDatabaseBackupSource` (implemented in a later phase).

Startup database initialization: resolve location → ensure data directory → open the SQLite connection → check migration state → apply pending migrations → confirm readiness. An existing database is never deleted or recreated (`EnsureDeleted` is never used). `DatabaseInitializer` returns a `DatabaseInitializationResult` and logs failures; the desktop startup fails the pipeline on a failed initialization.

`DatabaseHealthService` checks file existence, SQLite connectivity, migration status, and a basic read, returning a structured `DatabaseHealthResult` without exposing raw exceptions to the UI.

DbContext lifetime: scoped contexts are created per unit of work via DI; the initializer and health service use short-lived contexts from `IDbContextFactory<DhirDharDbContext>`. No static or long-lived shared DbContext exists. Domain and Application layers do not reference Entity Framework Core.

The initial migration (`InitialCreate`) intentionally contains no production tables; it establishes migration infrastructure for Phase 3 entities.

## Global Exception Handling

`App.xaml.cs` hooks:

- `Application.UnhandledException`
- `AppDomain.CurrentDomain.UnhandledException`
- `TaskScheduler.UnobservedTaskException`

Unexpected exceptions are logged (Critical/Error) and, where possible, prevented from silently terminating the application. Technical stack traces are not shown in user-facing messages. A dedicated error-reporting system is out of scope for Phase 1.

## Future Google Drive Integration Location

Google Drive backup is planned for a later phase:

- **Configuration**: `BackupOptions` (`DhirDhar.Infrastructure/Configuration`)
- **Abstraction**: `IDatabaseBackupSource` / `IDatabaseBackupSnapshot` (`DhirDhar.Application/Abstractions/Persistence/Backup`) — prepared in Phase 2
- **Implementation**: `DhirDhar.Infrastructure/Storage`

## Future Android Reuse Strategy

The Domain and Application layers are framework-agnostic and contain no desktop or Windows-specific dependencies. They can be referenced by a future Android project directly. Infrastructure abstractions (persistence, services) are declared in the Application layer precisely so Android can provide its own implementations.

## Testing

Tests are located under `tests/` and use isolated temporary SQLite databases (never the production database):

- **DhirDhar.Domain.Tests** — domain foundation and architecture (dependency direction) tests
- **DhirDhar.Application.Tests** — result pattern and DI registration tests
- **DhirDhar.Infrastructure.Tests** — DI registration, path resolution, DbContext creation, SQLite connection, database initialization, migration execution and idempotency, database health, unit of work, transaction commit/rollback, repository infrastructure, and invalid configuration/failure handling
