# PropFlow

A modular property operations platform for work orders, bulk actions, resident communication, and operational history. It complements property-management systems through integration adapters.

## Current milestone

Milestone 2 implements tenant-aware Identity login, secure cookies and CSRF protection, organization membership and capability checks, PostgreSQL persistence/migrations, database readiness, and a minimal work/vendor-assignment API with an append-only timeline. The web interface and polished bulk workflow are milestone 3. There are no live resident messages or production PMS integrations.

## Get started

See [local development](docs/local-development.md) for first organization/admin setup, HTTPS, and runtime configuration. The API requires a migrated PostgreSQL database and a restricted database account. It will reject an owner/superuser connection.

```powershell
dotnet restore PropFlow.slnx --locked-mode
dotnet build PropFlow.slnx --configuration Release --no-restore
dotnet run --project tests/PropFlow.FoundationChecks --configuration Release --no-build
dotnet test tests/PropFlow.UnitTests --configuration Release --no-build
dotnet test tests/PropFlow.IntegrationTests --configuration Release --no-build
```

Integration tests use disposable real PostgreSQL containers; Docker must be running. No existing database is changed. Dependencies are pinned and locked. GitHub Actions runs the foundation checks and both test suites.

## Repository

- `src/PropFlow.Api`: thin HTTP endpoints, session/CSRF policies, health and error handling.
- `src/PropFlow.Application`: capabilities, tenant context, work-use-case contracts and events.
- `src/PropFlow.Domain`: tenant entities, assignment semantics and immutable history.
- `src/PropFlow.Infrastructure`: Identity, EF Core, PostgreSQL RLS, migrations and persistence services.
- `tools/PropFlow.Admin`: explicit migration, runtime-role configuration and initial organization provisioning.
- `tests`: foundation boot checks, xUnit unit tests for domain/application logic, and PostgreSQL/API integration tests.
- `apps/web`: reserved Next.js boundary; no runnable frontend yet.
- `docs`: [architecture](docs/architecture.md), [milestones](docs/milestones.md), [backlog](docs/backlog.md), [API](docs/api.md), [security audit](docs/audit-security.md), [communications audit](docs/audit-communications.md), and original handoff.

Design and implementation are independent. Competitor products are market references only; their source, UI and proprietary workflows are not copied.
